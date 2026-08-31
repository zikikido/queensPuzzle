@echo off
rem Downloads the Firebase UPM tarballs referenced by Packages\manifest.json
rem into the GooglePackages folder. Bump VERSION together with the manifest.
setlocal
set VERSION=13.13.0
rem Script lives in SetupProjectScript\ - work from the repo root
cd /d "%~dp0.."
if not exist GooglePackages mkdir GooglePackages
for %%p in (app remote-config analytics crashlytics) do (
    echo downloading com.google.firebase.%%p-%VERSION%.tgz
    curl -sfL -o "GooglePackages\com.google.firebase.%%p-%VERSION%.tgz" "https://dl.google.com/games/registry/unity/com.google.firebase.%%p/com.google.firebase.%%p-%VERSION%.tgz" || goto :fail
)
echo done
exit /b 0
:fail
echo FAILED
exit /b 1
