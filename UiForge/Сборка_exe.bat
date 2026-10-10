@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ============================================
echo   UiForge - сборка готовой программы (exe)
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Не найден .NET SDK. Установите .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

if exist publish rmdir /s /q publish

dotnet publish UiForge.App\UiForge.App.csproj -c Release -r win-x86 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true -o publish

if errorlevel 1 (
    echo.
    echo Сборка не удалась. Скопируйте текст ошибки выше.
    pause
    exit /b 1
)

echo.
echo Готово! Программа лежит в папке "publish":
echo   UiForge.exe  + pckdll_x86.dll + pckdll_x86_b.dll  (эти три файла должны лежать вместе)
echo Можно заархивировать папку publish и отправлять друзьям - ничего устанавливать не нужно.
echo.
pause
