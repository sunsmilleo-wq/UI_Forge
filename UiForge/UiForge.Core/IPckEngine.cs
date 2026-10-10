using UiForge.Core.Model;

namespace UiForge.Core
{
    /// <summary>
    /// Контракт движка чтения/записи PCK-архивов.
    /// UI и остальная логика PCK Manager работают ТОЛЬКО через этот интерфейс
    /// и не должны знать, что за реализация стоит за ним —
    /// обёртка над старой pckdll_x86.dll (WinPckEngine) или наш собственный
    /// движок (NativePckEngine).
    ///
    /// Это позволяет:
    ///  - подменять реализацию без переписывания UI;
    ///  - сверять два движка друг с другом на одних и тех же файлах
    ///    (см. план: WinPckEngine как эталон для NativePckEngine).
    /// </summary>
    public interface IPckEngine : IDisposable
    {
        /// <summary>Человекочитаемое имя реализации, например "WinPck (pckdll_x86.dll)".</summary>
        string EngineName { get; }

        /// <summary>Открыть архив по пути. Бросает исключение, если файл не является валидным PCK.</summary>
        void Open(string path);

        /// <summary>Закрыть текущий открытый архив и освободить ресурсы.</summary>
        void Close();

        /// <summary>Проверить, является ли текущий открытый файл валидным PCK.</summary>
        bool IsValid();

        /// <summary>Общее количество файлов (без учёта папок) в архиве.</summary>
        int FileCount();

        /// <summary>Корневая запись дерева архива.</summary>
        PckEntry GetRoot();

        /// <summary>Найти запись по относительному пути внутри архива.</summary>
        PckEntry? FindByPath(string relativePath);

        /// <summary>Прочитать содержимое одного файла из архива в память.</summary>
        byte[] ReadFile(PckEntry entry);

        /// <summary>Извлечь один файл на диск.</summary>
        void ExtractFile(PckEntry entry, string destinationPath);

        /// <summary>Извлечь папку (рекурсивно) на диск.</summary>
        void ExtractFolder(PckEntry folder, string destinationDir);

        /// <summary>Извлечь весь архив на диск.</summary>
        void ExtractAll(string destinationDir);

        /// <summary>
        /// Заменить содержимое существующего файла внутри архива и сохранить
        /// изменения на диск. После вызова архив автоматически переоткрывается
        /// (дерево нужно перечитать заново — старые PckEntry могут быть неактуальны).
        /// </summary>
        void ReplaceFileContent(PckEntry entry, byte[] newContent);

        /// <summary>
        /// Добавить или заменить файл в архиве напрямую с диска (без промежуточного
        /// чтения в память) — используется для "перетащить файл", "заменить из
        /// внешнего файла" и т.п. Имя файла внутри архива берётся из имени
        /// <paramref name="sourceFilePathOnDisk"/>. Если по этому имени в указанной
        /// папке уже есть файл — он заменяется, иначе добавляется новый.
        /// </summary>
        /// <param name="destinationFolderRelativePath">
        /// Папка внутри архива (например "configs"), пустая строка — корень.
        /// </param>
        void AddOrReplaceFile(string destinationFolderRelativePath, string sourceFilePathOnDisk);

        /// <summary>
        /// Удалить файл (или папку целиком) из архива и сохранить изменения на
        /// диск. После вызова архив автоматически переоткрывается — дерево
        /// нужно перечитать заново.
        /// </summary>
        void DeleteEntry(PckEntry entry);

        /// <summary>
        /// Переименовать файл или папку (только новое имя, не путь). После вызова
        /// архив автоматически переоткрывается — дерево нужно перечитать заново.
        /// </summary>
        void RenameEntry(PckEntry entry, string newName);

        // --- Остальные операции изменения (добавляются по мере необходимости) ---

        // void DeleteEntry(PckEntry entry);
        // void RenameEntry(PckEntry entry, string newName);
    }
}
