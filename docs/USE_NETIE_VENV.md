# Always use Netie's venv — not system Python

Wrong (what happened earlier):
  python -m pip install ocrmypdf
  → lands in C:\Users\...\AppData\Roaming\Python\Python314\  (Netie ignores this)

Correct:
  D:\Netie Space\.venv\Scripts\python.exe -m pip install ocrmypdf pytesseract
  D:\Netie Space\.venv\Scripts\python.exe scripts\pdf-stress\test_pdf_ops.py

Tesseract binary (separate from pip):
  winget install UB-Mannheim.TesseractOCR
  Default path: C:\Program Files\Tesseract-OCR\tesseract.exe
  Netie auto-prepends this when spawning convert_engine.
