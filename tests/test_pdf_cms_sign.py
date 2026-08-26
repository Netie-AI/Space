"""Contract checks for PKCS#7 PDF cert sign (no iText)."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SIGNER = ROOT / "src/NetieSpace/Services/PdfCmsSigner.cs"
PREVIEW = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs"
XAML = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml"
TEST_CS = ROOT / "tests/NetieSpace.Tests/PdfCmsSignerTests.cs"
CSPROJ = ROOT / "src/NetieSpace/NetieSpace.csproj"
TESTPROJ = ROOT / "tests/NetieSpace.Tests/NetieSpace.Tests.csproj"
PARKING = ROOT / "PARKING_LOT.md"


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_signer_is_cms_not_itext():
    src = _read(SIGNER)
    assert "adbe.pkcs7.detached" in src
    assert "SignedCms" in src
    assert "using iText" not in src
    assert "iTextSharp" not in src
    assert "CreateMinimalPdf" in src
    assert "SignFile" in src
    assert "Leave original bytes untouched on failure" in src or "untouched on failure" in src
    assert "/Annots" in src
    assert "/AP << /N" in src
    assert "RewritePageWithAnnot" in src
    assert "BuildAppearanceStream" in src
    assert "TryVerify" in src
    assert "CheckSignature" in src
    assert "self-check" in src
    assert "NeedsClassicXrefRewrite" in src


def test_ui_and_packages():
    xaml = _read(XAML)
    cs = _read(PREVIEW)
    app = _read(CSPROJ)
    tests = _read(TESTPROJ)
    assert 'Header="Cert sign (PFX)..."' in xaml
    assert "PdfCertSign_Click" in xaml
    assert "PdfCertSign_Click" in cs
    assert "PdfCmsSigner.SignFile" in cs
    assert "RewriteClassicXref" in cs
    assert "NeedsClassicXrefRewrite" in cs
    assert "Could not rewrite xref-stream/hybrid" in cs
    assert "WriteAllBytes(path, backup)" in cs
    edit = _read(ROOT / "src/NetieSpace/Services/PdfEditService.cs")
    assert "RewriteClassicXref" in edit
    assert "PdfDocumentOpenMode.Modify" in edit
    assert "System.Security.Cryptography.Pkcs" in app
    assert "PdfCmsSigner.cs" in tests
    assert "System.Security.Cryptography.Pkcs" in tests


def test_xunit_and_parking():
    unit = _read(TEST_CS)
    parking = _read(PARKING)
    assert "Sign_embeds_detached_pkcs7_and_keeps_hello" in unit
    assert "Sign_self_verifies_detached_cms" in unit
    assert "NeedsClassicXrefRewrite_detects_missing_trailer" in unit
    assert "NeedsClassicXrefRewrite_detects_hybrid_trailer_plus_xref_stream" in unit
    assert "Sign_refuses_hybrid_trailer_plus_xref_stream" in unit
    assert "Sign_refuses_catalog_without_pages_ref" in unit
    signer = _read(SIGNER)
    assert "/Type /XRef" in signer
    assert "NeedsClassicXrefRewrite(pdf)" in signer
    assert "could not read /Pages" in signer
    assert "?? 2" not in signer
    assert "hybrid" in signer.lower() or "hybrid" in _read(TEST_CS).lower()
    assert "Cryptographic digital signature" in parking
    assert "PdfCmsSigner" in parking or "PKCS#7" in parking


if __name__ == "__main__":
    test_signer_is_cms_not_itext()
    test_ui_and_packages()
    test_xunit_and_parking()
    print("PASS test_pdf_cms_sign")
