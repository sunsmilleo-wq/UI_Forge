using System.Runtime.InteropServices;
using System.Text;

namespace UiForge.Engine.WinPck
{
    /// <summary>Слот B: те же функции, но из второй копии DLL (см. NativeLoader).</summary>
    internal static class NativeB
    {
        // "pckdll" — логическое имя, не настоящее имя файла. Реальный файл
        // (pckdll_x86.dll или pckdll_x64.dll) подбирается в рантайме через
        // Resolve() ниже, в зависимости от разрядности процесса. Так, когда
        // появится 64-битная сборка pckdll (см. BUILD_X64.md), достаточно
        // положить pckdll_x64.dll рядом с exe и переключить PlatformTarget
        // проекта на x64 — ни одной строчки P/Invoke-кода менять не придётся.
        private const string Dll = "pckdll_b";  // слот B → pckdll_x86_b.dll
        private const int PCK_ENTRY_TYPE_FOLDER = 4;
        private const int PCK_ENTRY_TYPE_DOTDOT = 8;
        internal const int PckEntryTypeFolder = PCK_ENTRY_TYPE_FOLDER;
        internal const int PckEntryTypeDotDot = PCK_ENTRY_TYPE_DOTDOT;

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern PCKRTN pck_open(string lpszFile);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PCKRTN pck_close();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pck_IsValidPck(); // BOOL

        // ВАЖНО: буфер ПЕРВЫМ, entry ВТОРЫМ — обратный порядок относительно
        // того, что мы тестировали в пробнике. Буфер должен быть заведомо
        // большим (MAX_PATH_PCK_260 = 260 символов в оригинале), т.к. размера
        // буфера функция не принимает и не проверяет.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int pck_getNodeRelativePath(StringBuilder outPath, IntPtr lpFileEntry); // BOOL

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr pck_getFileEntryByPath(string path); // LPCENTRY

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr pck_getRootNode(); // LPCENTRY

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong pck_filesize();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint pck_filecount(); // БЕЗ аргументов!

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong pck_getFileSizeInEntry(IntPtr lpFileEntry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong pck_getCompressedSizeInEntry(IntPtr lpFileEntry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint pck_getFoldersCountInEntry(IntPtr lpFileEntry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint pck_getFilesCountInEntry(IntPtr lpFileEntry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong pck_getFileOffset(IntPtr lpFileEntry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint pck_listByNode(IntPtr lpFileEntry, IntPtr param, ShowListCallback callback);

        // char* buffer, size_t sizeOfBuffer — сырые байты (декомпрессированное
        // содержимое файла), НЕ широкие символы. Буфер должен быть заранее
        // выделен размером не меньше entry.OriginalSize.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PCKRTN pck_GetSingleFileData(IntPtr lpFileEntry, byte[] buffer, UIntPtr sizeOfBuffer);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern PCKRTN pck_ExtractAllFiles(string destDir);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PCKRTN pck_ExtractFilesByEntrys(IntPtr[] entries, int count,
            [MarshalAs(UnmanagedType.LPWStr)] string destDir);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pck_isThreadWorking(); // BOOL

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pck_isLastOptSuccess(); // BOOL

        // Схема "пометить, затем подтвердить" — сначала помечаем запись на
        // удаление, затем отдельным вызовом фиксируем изменение на диске.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PCKRTN pck_DeleteEntry(IntPtr lpFileEntry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PCKRTN pck_DeleteEntrySubmit();

        // Схема "пометить, затем подтвердить" — работает и для файлов, и для папок.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern PCKRTN pck_RenameEntry(IntPtr lpFileEntry, string lpszReplaceString);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern PCKRTN pck_RenameSubmit();

        // Внимание: эта функция САМА открывает/закрывает архив через тот же
        // глобальный хэндл, что и pck_open/pck_close — поэтому вызывать её
        // можно ТОЛЬКО когда наша собственная сессия уже закрыта, иначе
        // конфликт из-за общего состояния внутри DLL.
        // Несмотря на имя ("Add"), реально ЗАМЕНЯЕТ содержимое уже
        // существующего по пути файла — если пути не существует, вернёт
        // WINPCK_NOTFOUND, новый файл не создаст.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern PCKRTN do_AddFileToPckFile(string lpszFilePathSrc, string szDstPckFile,
            string lpszPathInPckToAdd, int level = 9);
    }
}
