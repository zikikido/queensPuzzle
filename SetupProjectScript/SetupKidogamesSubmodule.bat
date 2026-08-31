@echo off
REM Run once after a fresh clone.
REM Fetches the Assets/kidogamesCode submodule with ONLY the Common folder
REM checked out (sparse checkout + blob filter, so other folders are never downloaded).
setlocal
REM Script lives in SetupProjectScript\ - work from the repo root
cd /d "%~dp0.."

set SUB_PATH=Assets/kidogamesCode
set SUB_URL=git@github.com:zikikido/kidogamesCode.git

REM Commit the superproject pins the submodule to
for /f "tokens=3" %%i in ('git ls-tree HEAD %SUB_PATH%') do set COMMIT=%%i

if exist "%SUB_PATH%\.git" (
    echo Submodule already initialized - syncing to pinned commit %COMMIT%
    git -C %SUB_PATH% fetch origin %COMMIT%
    git -C %SUB_PATH% checkout %COMMIT%
    goto :done
)

git submodule init
git clone --filter=blob:none --no-checkout %SUB_URL% %SUB_PATH%
git -C %SUB_PATH% sparse-checkout set --no-cone /Common/ /Common.meta /.gitignore
git -C %SUB_PATH% fetch origin %COMMIT%
git -C %SUB_PATH% checkout %COMMIT%
git submodule absorbgitdirs %SUB_PATH%

echo Done: %SUB_PATH% at %COMMIT% (Common folder only)
:done
endlocal
