@echo off
rem 发布 EasyWin:依赖框架的单文件(需目标机器装有 .NET 8 桌面运行时)
rem 如需免装运行时,把下面的 --self-contained false 改为 true(体积约 +150MB)
cd /d "%~dp0..\src\EasyWin"
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "%~dp0..\dist"
echo.
echo 发布完成: dist\EasyWin.exe
pause
