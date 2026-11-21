"""One-page AcroForm with a Name widget at a known rect (origin bottom-left)."""
from pathlib import Path

out = Path(r"D:\Space\src\NetieSpace\Assets\pdfjs\__stress\acroform.pdf")
out.parent.mkdir(parents=True, exist_ok=True)

# Widget: x=72..252, y=690..714  (under printed "Name" at y=720)
parts = []

def obj(n, body):
    parts.append(f"{n} 0 obj\n{body}\nendobj\n")

obj(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm 6 0 R >>")
obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R "
    "/Resources << /Font << /F1 5 0 R >> >> /Annots [7 0 R] >>")
stream = b"BT /F1 12 Tf 72 720 Td (Name) Tj ET"
obj(4, f"<< /Length {len(stream)} >>\nstream\n{stream.decode('latin1')}\nendstream")
obj(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
obj(6, "<< /Fields [7 0 R] /NeedAppearances true /DA (/Helv 10 Tf 0 g) "
    "/DR << /Font << /Helv 5 0 R >> >> >>")
obj(7, "<< /Type /Annot /Subtype /Widget /FT /Tx /T (Name) /F 4 "
    "/Rect [72 690 252 714] /P 3 0 R /DA (/Helv 10 Tf 0 g) /MK << /BC [0.17 0.42 0.69] >> >>")

body = "%PDF-1.4\n" + "".join(parts)
offsets = []
pos = 0
text = "%PDF-1.4\n"
# rebuild with tracked offsets
chunks = ["%PDF-1.4\n"]
cursor = len(chunks[0])
bodies = [
    "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm 6 0 R >>\nendobj\n",
    "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
    "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> /Annots [7 0 R] >>\nendobj\n",
    "4 0 obj\n<< /Length 32 >>\nstream\nBT /F1 12 Tf 72 720 Td (Name) Tj ET\nendstream\nendobj\n",
    "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
    "6 0 obj\n<< /Fields [7 0 R] /NeedAppearances true /DA (/Helv 10 Tf 0 g) /DR << /Font << /Helv 5 0 R >> >> >>\nendobj\n",
    "7 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Tx /T (Name) /F 4 /Rect [72 690 252 714] /P 3 0 R /DA (/Helv 10 Tf 0 g) /MK << /BC [0.17 0.42 0.69] >> >>\nendobj\n",
]
# fix stream length
raw_stream = b"BT /F1 12 Tf 72 720 Td (Name) Tj ET"
bodies[3] = (
    f"4 0 obj\n<< /Length {len(raw_stream)} >>\nstream\n"
    + raw_stream.decode("latin1")
    + "\nendstream\nendobj\n"
)

data = bytearray(b"%PDF-1.4\n")
offs = [0]
for b in bodies:
    offs.append(len(data))
    data.extend(b.encode("latin1"))

xref_pos = len(data)
xref = [f"xref\n0 8\n0000000000 65535 f \n"]
for o in offs[1:]:
    xref.append(f"{o:010d} 00000 n \n")
data.extend("".join(xref).encode("latin1"))
data.extend(
    f"trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n{xref_pos}\n%%EOF\n".encode("latin1")
)
out.write_bytes(bytes(data))
print(f"wrote {out} bytes={out.stat().st_size} startxref={xref_pos}")
