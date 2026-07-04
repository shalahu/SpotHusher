@echo off
setlocal enabledelayedexpansion

dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true --self-contained false /p:AssemblyName="SpotHusher_x64"
dotnet publish -c Release -r win-x86 /p:PublishSingleFile=true --self-contained false /p:AssemblyName="SpotHusher_x86"

set "X64_DIR=bin\Release\net10.0-windows\win-x64\publish"
set "X86_DIR=bin\Release\net10.0-windows\win-x86\publish"

set "ZIP_X64=SpotHusher_x64_V1.4.zip"
set "ZIP_X86=SpotHusher_x86_V1.4.zip"

echo --------------------------------------------------
echo Starting packaging process...
echo --------------------------------------------------

if exist "%ZIP_X64%" del /q "%ZIP_X64%"
if exist "%ZIP_X86%" del /q "%ZIP_X86%"

if exist "%X64_DIR%\SpotHusher_x64.exe" (
    echo Packaging x64 version...
    powershell -Command "Compress-Archive -Path '%X64_DIR%\SpotHusher_x64.exe' -DestinationPath '%ZIP_X64%' -Force"
    echo [SUCCESS] Generated: %ZIP_X64%
) else (
    echo [ERROR] x64 build output not found. Please check your .NET framework path.
)

if exist "%X86_DIR%\SpotHusher_x86.exe" (
    echo Packaging x86 version...
    powershell -Command "Compress-Archive -Path '%X86_DIR%\SpotHusher_x86.exe' -DestinationPath '%ZIP_X86%' -Force"
    echo [SUCCESS] Generated: %ZIP_X86%
) else (
    echo [ERROR] x86 build output not found. Please check your .NET framework path.
)

echo --------------------------------------------------
echo Packaging process completed!
echo --------------------------------------------------
pause
