@echo off

dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true --self-contained false /p:AssemblyName="SpotHusher_x64"

dotnet publish -c Release -r win-x86 /p:PublishSingleFile=true --self-contained false /p:AssemblyName="SpotHusher_x86"