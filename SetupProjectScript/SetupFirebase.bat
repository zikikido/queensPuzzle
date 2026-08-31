@echo off
rem Downloads the Firebase UPM tarballs referenced by Packages\manifest.json.
rem Run from anywhere; files land next to this script. Bump VERSION together with the manifest.
setlocal
set VERSION=13.13.0
cd /d "%~dp0"
for %%p in (app remote-config analytics crashlytics) do (
    echo downloading com.google.firebase.%%p-%VERSION%.tgz
    curl -sfL -o "com.google.firebase.%%p-%VERSION%.tgz" "https://dl.google.com/games/registry/unity/com.google.firebase.%%p/com.google.firebase.%%p-%VERSION%.tgz" || goto :fail
)
echo done
exit /b 0
:fail
echo FAILED
exit /b 1
