@echo off

SET PKGVER=%1
SET APIKEY=%2
SET SOURCE=https://api.nuget.org/v3/index.json

dotnet nuget push Karambolo.Extensions.Logging.File.%PKGVER%.nupkg -k %APIKEY% -s %SOURCE%
IF %ERRORLEVEL% NEQ 0 goto:eof
