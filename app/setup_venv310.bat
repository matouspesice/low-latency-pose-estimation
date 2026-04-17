@echo off
REM One-time: create app\.venv310 with Python 3.10 and install requirements (fixes RapidOCR on Python 3.14).
set PY310=%LocalAppData%\Programs\Python\Python310\python.exe
if not exist "%PY310%" (
  echo Python 3.10 not found at: %PY310%
  echo Install from https://www.python.org/downloads/ or adjust PY310 in this script.
  exit /b 1
)
cd /d "%~dp0"
"%PY310%" -m venv .venv310
call .venv310\Scripts\activate.bat
python -m pip install -U pip
pip install -r requirements.txt
echo.
echo Done. Example run:
echo   .venv310\Scripts\python.exe ocr_clock_live.py --camera-mode flir --device cuda
pause
