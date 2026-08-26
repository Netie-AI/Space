"""Contract checks for PKCS#7 PDF cert sign (no iText)."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SIGNER = ROOT / "src/NetieSpace/Services/PdfCmsSigner.cs"
PREVIEW = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs"
XAML = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml"
TEST_CS = ROOT / "tests/NetieSpace.Tests/PdfCmsSignerTests.cs"
XREF = ROOT / "src/NetieSpace/Services/PdfClassicXref.cs"
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
    assert "LoadPfx" in src
    assert "EphemeralKeySet" in src
    assert "PersistKeySet" in src
    assert "Leave original bytes untouched on failure" in src or "untouched on failure" in src
    assert "/Annots" in src
    assert "/AP << /N" in src
    assert "RewritePageWithAnnot" in src
    assert "BuildAppearanceStream" in src
    assert "TryVerify" in src
    assert "CheckSignature" in src
    assert "self-check" in src
    assert "NeedsClassicXrefRewrite" in src
    assert "IndexOfObj" in src
    assert "12 0 obj" in src
    assert "CreateMinimalPdfWithIndirectKids" in src
    assert "CreateMinimalPdfWithIndirectAnnots" in src
    assert "CreateMinimalPdfCompactNames" in src
    assert "/Kids[3 0 R]" in src
    assert "FirstPageFromKids" in src
    assert "/Kids 5 0 R" in src
    assert "/Annots 5 0 R" in src
    tests = _read(TEST_CS)
    assert "TryReadObject_does_not_match_12_as_2" in tests
    assert "TryFirstPageRef_resolves_indirect_kids_array" in tests
    assert "Sign_resolves_indirect_kids_onto_first_page" in tests


def test_ui_and_packages():
    xaml = _read(XAML)
    cs = _read(PREVIEW)
    app = _read(CSPROJ)
    tests = _read(TESTPROJ)
    assert 'Header="Cert sign (PFX)..."' in xaml
    assert "PdfCertSign_Click" in xaml
    assert "PdfCertSign_Click" in cs
    assert "PdfCmsSigner.SignFile" in cs
    assert "PdfCmsSigner.LoadPfx" in cs
    assert "RewriteClassicXref" in cs
    assert "NeedsClassicXrefRewrite" in cs
    assert "Could not rewrite xref-stream/hybrid" in cs
    assert "WriteAllBytes(path, backup)" in cs
    edit = _read(ROOT / "src/NetieSpace/Services/PdfEditService.cs")
    xref = _read(XREF)
    assert "RewriteClassicXref" in edit
    assert "PdfClassicXref.RewriteFile" in edit
    assert "PdfDocumentOpenMode.Modify" in xref
    assert "still left an xref-stream" in xref
    assert "PdfClassicXref.cs" in tests
    assert "System.Security.Cryptography.Pkcs" in app
    assert "PdfCmsSigner.cs" in tests
    assert "System.Security.Cryptography.Pkcs" in tests


def test_xunit_and_parking():
    unit = _read(TEST_CS)
    parking = _read(PARKING)
    assert "Sign_embeds_detached_pkcs7_and_keeps_hello" in unit
    assert "TryReadObject_does_not_match_12_as_2" in unit
    assert "Sign_self_verifies_detached_cms" in unit
    assert "NeedsClassicXrefRewrite_detects_missing_trailer" in unit
    assert "NeedsClassicXrefRewrite_detects_hybrid_trailer_plus_xref_stream" in unit
    assert "Sign_refuses_hybrid_trailer_plus_xref_stream" in unit
    assert "Sign_refuses_catalog_without_pages_ref" in unit
    assert "TryFirstPageRef_resolves_indirect_kids_array" in unit
    assert "Sign_resolves_indirect_kids_onto_first_page" in unit
    assert "Sign_inlines_indirect_annots_array_onto_first_page" in unit
    assert "TryFirstPageRef_resolves_compact_kids_and_type" in unit
    assert "Sign_compact_names_stamps_first_page" in unit
    assert "Sign_in_app_pdf_stamps_page_4" in unit
    assert "RewriteFile_strips_hybrid_marker_then_sign_verifies" in unit
    assert "RewriteFile_strips_incremental_xref_stream_then_sign_verifies" in unit
    assert "RewriteFile_first_gen_xref_stream_then_sign_verifies" in unit
    assert "PdfSharp_new_document_is_classic_or_rewritable" in unit
    assert "Sign_after_disk_pfx_load_matches_ui" in unit
    assert "LoadPfx_signs_openssl_playbook_pfx" in unit
    xref = _read(XREF)
    assert "TryStripToClassic" in xref
    assert "FindEofEnds" in xref
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
