@echo off
chcp 65001 >nul
title 啟動 OpenCam (螢幕錄影工具)
echo 正在啟動 OpenCam (螢幕錄影工具)...
start "" "%~dp0src\ScreenRecorder.UI\bin\Release\net10.0\OpenCam.exe"

