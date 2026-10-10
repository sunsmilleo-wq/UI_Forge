using System.Text;
using UiForge.Core;
using UiForge.Core.Model;

namespace UiForge.Engine.WinPck
{
    /// <summary>
    /// Реализация <see cref="IPckEngine"/> поверх pckdll_x86.dll.
    /// Сигнатуры взяты из реальных исходников (stsm85/WinPCK, PckDll/include/pck_handle.h),
    /// а не восстановлены по ассемблеру — весь публичный контракт этого класса
    /// можно считать надёжным.
    /// </summary>
    public sealed class WinPckEngineB : IPckEngine
    {
        private bool _isOpen;
        private string? _openPath;
        // Держим делегат живым на всё время вызова pck_listByNode, иначе GC
        // может собрать его раньше, чем неуправляемый код успеет вызвать callback.
        private ShowListCallback? _activeCallback;

        public string EngineName => "WinPck слот B (pckdll_x86_b.dll)";

        public void Open(string path)
        {
            var result = NativeB.pck_open(path);
            if (result != PCKRTN.WINPCK_OK)
                throw new InvalidOperationException($"pck_open вернула {result} для файла '{path}'.");

            _isOpen = true;
            _openPath = path;

            if (!IsValid())
            {
                _isOpen = false;
                throw new InvalidOperationException($"Файл '{path}' открылся, но не прошёл проверку IsValidPck.");
            }
        }

        public void Close()
        {
            if (!_isOpen) return;
            NativeB.pck_close();
            _isOpen = false;
        }

        public bool IsValid()
        {
            EnsureOpen();
            return NativeB.pck_IsValidPck() != 0;
        }

        public int FileCount()
        {
            EnsureOpen();
            return (int)NativeB.pck_filecount();
        }

        public PckEntry GetRoot()
        {
            EnsureOpen();
            IntPtr root = NativeB.pck_getRootNode();
            if (root == IntPtr.Zero)
                throw new InvalidOperationException("pck_getRootNode вернула NULL.");

            return BuildEntry(root, isRoot: true);
        }

        public PckEntry? FindByPath(string relativePath)
        {
            EnsureOpen();
            IntPtr node = NativeB.pck_getFileEntryByPath(relativePath);
            return node == IntPtr.Zero ? null : BuildEntry(node, isRoot: false);
        }

        public byte[] ReadFile(PckEntry entry)
        {
            EnsureOpen();
            if (entry.IsDirectory)
                throw new InvalidOperationException("Нельзя прочитать содержимое папки.");

            IntPtr node = ResolveNode(entry);
            int size = (int)entry.OriginalSize;
            var buffer = new byte[Math.Max(size, 0)];

            if (size > 0)
            {
                var rtn = NativeB.pck_GetSingleFileData(node, buffer, (UIntPtr)size);
                if (rtn != PCKRTN.WINPCK_OK)
                    throw new InvalidOperationException($"pck_GetSingleFileData вернула {rtn} для '{entry.RelativePath}'.");
            }

            return buffer;
        }

        public void ExtractFile(PckEntry entry, string destinationPath)
        {
            EnsureOpen();
            IntPtr node = ResolveNode(entry);
            string tempDir = Path.Combine(Path.GetTempPath(), "pckmanager_extract_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var entries = new[] { node };
                var rtn = NativeB.pck_ExtractFilesByEntrys(entries, 1, tempDir);
                if (rtn != PCKRTN.WINPCK_OK)
                    throw new InvalidOperationException($"pck_ExtractFilesByEntrys вернула {rtn}.");

                string extracted = Path.Combine(tempDir, Path.GetFileName(entry.RelativePath));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(extracted, destinationPath, overwrite: true);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
            }
        }

        public void ExtractFolder(PckEntry folder, string destinationDir) =>
            ExtractFolderRecursive(folder, folder, destinationDir);

        /// <summary>
        /// Извлекает файлы по одному (а не всей папкой через один вызов DLL) —
        /// намеренно: pck_ExtractFilesByEntrys кладёт файлы ПЛОСКО, по короткому
        /// имени, и если бы мы извлекали много файлов за один вызов, два файла
        /// с одинаковым именем из разных подпапок затёрли бы друг друга ещё до
        /// того, как мы успели бы разложить их по правильным местам. По одному —
        /// медленнее, зато корректно.
        /// </summary>
        private void ExtractFolderRecursive(PckEntry currentFolder, PckEntry rootOfExtraction, string destRoot)
        {
            foreach (var child in currentFolder.Children)
            {
                if (child.IsDirectory)
                {
                    ExtractFolderRecursive(child, rootOfExtraction, destRoot);
                }
                else
                {
                    string relativeFromRoot = string.IsNullOrEmpty(rootOfExtraction.RelativePath)
                        ? child.RelativePath
                        : child.RelativePath.Substring(rootOfExtraction.RelativePath.Length).TrimStart('\\');

                    string destPath = Path.Combine(destRoot, relativeFromRoot);
                    ExtractFile(child, destPath);
                }
            }
        }

        public void ExtractAll(string destinationDir)
        {
            EnsureOpen();
            Directory.CreateDirectory(destinationDir);
            var rtn = NativeB.pck_ExtractAllFiles(destinationDir);
            if (rtn != PCKRTN.WINPCK_OK)
                throw new InvalidOperationException($"pck_ExtractAllFiles вернула {rtn}.");
        }

        public void ReplaceFileContent(PckEntry entry, byte[] newContent)
        {
            if (entry.IsDirectory)
                throw new InvalidOperationException("Нельзя заменить содержимое папки.");

            string fileName = Path.GetFileName(entry.RelativePath);
            string folderPath = Path.GetDirectoryName(entry.RelativePath) ?? "";

            string tempDir = Path.Combine(Path.GetTempPath(), "pckmanager_replace_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, fileName);

            try
            {
                File.WriteAllBytes(tempFile, newContent);
                AddOrReplaceFile(folderPath, tempFile);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
            }
        }

        public void DeleteEntry(PckEntry entry)
        {
            EnsureOpen();
            if (_openPath is null)
                throw new InvalidOperationException("Внутренняя ошибка: путь открытого архива не сохранён.");

            IntPtr node = ResolveNode(entry);

            var markRtn = NativeB.pck_DeleteEntry(node);
            if (markRtn != PCKRTN.WINPCK_OK)
                throw new InvalidOperationException($"pck_DeleteEntry вернула {markRtn} для '{entry.RelativePath}'.");

            var submitRtn = NativeB.pck_DeleteEntrySubmit();

            // На всякий случай переоткрываем сессию — удаление перестраивает
            // архив физически, старые указатели узлов после этого не надёжны.
            string archivePath = _openPath;
            NativeB.pck_close();
            _isOpen = false;
            Open(archivePath);

            if (submitRtn != PCKRTN.WINPCK_OK)
                throw new InvalidOperationException($"pck_DeleteEntrySubmit вернула {submitRtn}.");
        }

        public void AddOrReplaceFile(string destinationFolderRelativePath, string sourceFilePathOnDisk)
        {
            EnsureOpen();
            if (_openPath is null)
                throw new InvalidOperationException("Внутренняя ошибка: путь открытого архива не сохранён.");
            if (!File.Exists(sourceFilePathOnDisk))
                throw new FileNotFoundException("Исходный файл не найден.", sourceFilePathOnDisk);

            string archivePath = _openPath;

            // do_AddFileToPckFile сама открывает архив через тот же глобальный
            // хэндл, что и наша сессия — поэтому сначала закрываем свою сессию,
            // иначе будет конфликт из-за общего состояния внутри DLL.
            NativeB.pck_close();
            _isOpen = false;

            var rtn = NativeB.do_AddFileToPckFile(sourceFilePathOnDisk, archivePath, destinationFolderRelativePath, 9);

            // Переоткрываем архив в любом случае, чтобы наша сессия снова была
            // рабочей (и дерево можно было перечитать), даже если запись не удалась.
            Open(archivePath);

            if (rtn != PCKRTN.WINPCK_OK)
                throw new InvalidOperationException(
                    $"do_AddFileToPckFile вернула {rtn} для папки '{destinationFolderRelativePath}' / файла " +
                    $"'{Path.GetFileName(sourceFilePathOnDisk)}'.");
        }

        // --- Внутреннее ---

        /// <summary>
        /// Строит PckEntry для узла, рекурсивно обходя детей через pck_listByNode.
        /// Мы сохраняем нативный IntPtr узла внутри Tag, чтобы позже можно было
        /// найти его снова для операций извлечения/удаления (см. ResolveNode).
        /// </summary>
        private PckEntry BuildEntry(IntPtr node, bool isRoot)
        {
            var relPathBuf = new StringBuilder(1024); // без размера в сигнатуре — берём с большим запасом
            NativeB.pck_getNodeRelativePath(relPathBuf, node);
            // DLL сама добавляет завершающий '\' для папок (см. GetCurrentNodeString в
            // исходниках) — убираем его, чтобы не путать Path.GetFileName и не плодить
            // двойные слэши при склейке путей файлов.
            string relPath = relPathBuf.ToString().TrimEnd('\\');

            var children = new List<PckEntry>();

            if (isRoot || true) // pck_listByNode сам проверяет, что узел — папка
            {
                var callback = new ShowListCallback((param, serial, name, entryType, clearSize, cipherSize, childNode) =>
                {
                    bool isFolder = (entryType & NativeB.PckEntryTypeFolder) != 0;
                    bool isDotDot = (entryType & NativeB.PckEntryTypeDotDot) != 0;
                    if (isDotDot) return; // не показываем ".." в дереве

                    string childRelPath = string.IsNullOrEmpty(relPath) ? name : relPath + "\\" + name;

                    if (isFolder)
                    {
                        // Рекурсивно строим содержимое подпапки сразу же (подходит для
                        // архивов среднего размера вроде interfaces.pck; для очень больших
                        // архивов в будущем стоит перейти на ленивую загрузку по клику).
                        // BuildEntry сам вызовет pck_getNodeRelativePath и получит верный путь.
                        children.Add(BuildEntry(childNode, isRoot: false));
                    }
                    else
                    {
                        children.Add(new PckEntry
                        {
                            Name = name,
                            RelativePath = childRelPath,
                            IsDirectory = false,
                            OriginalSize = (long)clearSize,
                            CompressedSize = (long)cipherSize,
                            NativeNodeCache = childNode,
                        });
                    }
                });

                _activeCallback = callback; // держим ссылку живой на время вызова
                NativeB.pck_listByNode(node, IntPtr.Zero, callback);
                _activeCallback = null;
            }

            return new PckEntry
            {
                Name = string.IsNullOrEmpty(relPath) ? "" : Path.GetFileName(relPath),
                RelativePath = relPath,
                IsDirectory = true,
                Children = children,
                NativeNodeCache = node,
            };
        }

        /// <summary>
        /// Достаёт нативный указатель узла обратно. Если PckEntry пришёл из нашего
        /// же дерева (BuildEntry), указатель уже закэширован. Иначе — ищем заново по пути.
        /// </summary>
        private IntPtr ResolveNode(PckEntry entry)
        {
            if (entry.NativeNodeCache != IntPtr.Zero)
                return entry.NativeNodeCache;

            IntPtr node = NativeB.pck_getFileEntryByPath(entry.RelativePath);
            if (node == IntPtr.Zero)
                throw new InvalidOperationException($"Не удалось найти узел по пути '{entry.RelativePath}'.");
            return node;
        }

        private void EnsureOpen()
        {
            if (!_isOpen)
                throw new InvalidOperationException("Архив не открыт. Сначала вызовите Open().");
        }

        public void RenameEntry(PckEntry entry, string newName)
        {
            EnsureOpen();
            if (_openPath is null)
                throw new InvalidOperationException("Внутренняя ошибка: путь открытого архива не сохранён.");
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("Новое имя не может быть пустым.", nameof(newName));

            IntPtr node = ResolveNode(entry);

            var markRtn = NativeB.pck_RenameEntry(node, newName);
            if (markRtn != PCKRTN.WINPCK_OK)
                throw new InvalidOperationException($"pck_RenameEntry вернула {markRtn} для '{entry.RelativePath}'.");

            var submitRtn = NativeB.pck_RenameSubmit();

            string archivePath = _openPath;
            NativeB.pck_close();
            _isOpen = false;
            Open(archivePath);

            if (submitRtn != PCKRTN.WINPCK_OK)
                throw new InvalidOperationException($"pck_RenameSubmit вернула {submitRtn}.");
        }

        public void Dispose() => Close();
    }
}
