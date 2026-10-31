@ECHO OFF
REM Run this script to format code manually.

dotnet format FileLogger.slnx --severity warn --report .
