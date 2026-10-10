using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UiForge.Engine.WinPck
{
    /// <summary>
    /// pckdll хранит ОДНУ глобальную сессию (pck_open/pck_close без хэндла), поэтому
    /// два архива одновременно через одну копию DLL открыть нельзя. Обход: слот A
    /// грузит pckdll_x86.dll, слот B — её байт-в-байт копию pckdll_x86_b.dll.
    /// Windows считает это двумя разными модулями со своими глобальными данными.
    /// Резолвер для сборки можно установить только один раз — поэтому он общий.
    /// </summary>
    internal static class NativeLoader
    {
        [ModuleInitializer]
        internal static void Init() =>
            NativeLibrary.SetDllImportResolver(typeof(NativeLoader).Assembly, Resolve);

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            string arch = Environment.Is64BitProcess ? "x64" : "x86";
            string? file = libraryName switch
            {
                "pckdll" => $"pckdll_{arch}.dll",
                "pckdll_b" => $"pckdll_{arch}_b.dll",
                _ => null
            };
            if (file is null) return IntPtr.Zero;

            // Сначала — явный путь рядом с программой. В «одиночном exe» (publish single-file) у сборки нет
            // собственной папки, и поиск по одному имени файла может не сработать.
            string full = System.IO.Path.Combine(AppContext.BaseDirectory, file);
            if (System.IO.File.Exists(full) && NativeLibrary.TryLoad(full, out IntPtr direct))
                return direct;

            if (NativeLibrary.TryLoad(file, assembly, searchPath, out IntPtr handle))
                return handle;

            throw new DllNotFoundException(
                $"Не найдена '{file}' рядом с exe. Положите pckdll_{arch}.dll и её копию " +
                $"pckdll_{arch}_b.dll в папку UiForge.App (они копируются при сборке).");
        }
    }
}
