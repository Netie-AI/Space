"""Generate a 3-page stress-test PDF that mimics the employment-form REFEREES table.

Known geometry (page 612x792, origin bottom-left), page 1:
  Table X edges: 50, 175, 285, 375, 465, 530, 562
  Table Y edges: 580, 620, 660, 700  (data2, data1, colheader rows), green bar 700-724
So the empty row-2 "Name" cell = x 50..175, y 580..620.
Page 2: Yes/No grid. Page 3: plain text.
"""
import sys

def esc(s):
    return s.replace("\\", r"\\").replace("(", r"\(").replace(")", r"\)")

X_EDGES = [50, 175, 285, 375, 465, 530, 562]
Y_EDGES = [580, 620, 660, 700]
GREEN_TOP = 724

def page1_stream():
    ops = []
    # Green header bar
    ops.append("0.78 0.92 0.71 rg")
    ops.append(f"{X_EDGES[0]} 700 {X_EDGES[-1]-X_EDGES[0]} {GREEN_TOP-700} re f")
    ops.append("0 0 0 RG 1 w")
    # Outer + inner grid lines
    for y in Y_EDGES + [GREEN_TOP]:
        ops.append(f"{X_EDGES[0]} {y} m {X_EDGES[-1]} {y} l S")
    for x in X_EDGES:
        ops.append(f"{x} {Y_EDGES[0]} m {x} {GREEN_TOP if x in (X_EDGES[0], X_EDGES[-1]) else Y_EDGES[-1]} l S")
    # Text
    def txt(x, y, size, s):
        ops.append(f"BT /F1 {size} Tf 1 0 0 1 {x} {y} Tm ({esc(s)}) Tj ET")
    ops.append("0 0 0 rg")
    txt(54, 707, 10, "12. REFEREES (Please give details of 2 referees)")
    heads = ["Name", "Occupation", "Company's", "Email Address", "Mobile", "Rel."]
    for i, h in enumerate(heads):
        txt(X_EDGES[i] + 4, 682, 9, h)
    row1 = ["DR RAYMOND WONG", "FYP PROFESSOR", "UM", "wjk@um.edu.my", "", ""]
    for i, v in enumerate(row1):
        if v:
            txt(X_EDGES[i] + 4, 636, 8, v)
    txt(50, 750, 14, "APPLICATION FOR EMPLOYMENT - STRESS PAGE 1")
    return "\n".join(ops)

def page2_stream():
    ops = ["0 0 0 RG 1 w"]
    def txt(x, y, size, s):
        ops.append(f"BT /F1 {size} Tf 1 0 0 1 {x} {y} Tm ({esc(s)}) Tj ET")
    txt(50, 750, 14, "13. GENERAL INFORMATION - STRESS PAGE 2")
    qs = ["a) Have you ever suffered from any illness?",
          "b) Are you currently under medication?",
          "c) Do you have any part-time job?",
          "d) Are you a member of any Professional Body?"]
    y = 700
    for q in qs:
        txt(60, y + 8, 10, q)
        # Yes/No boxes
        ops.append(f"470 {y} 40 26 re S")
        ops.append(f"515 {y} 40 26 re S")
        txt(478, y + 8, 9, "Yes")
        txt(524, y + 8, 9, "No")
        y -= 40
    # A small 2x2 table lower down
    for xx in (60, 200, 340):
        ops.append(f"{xx} 300 m {xx} 380 l S")
    for yy in (300, 340, 380):
        ops.append(f"60 {yy} m 340 {yy} l S")
    txt(64, 356, 9, "Class:")
    return "\n".join(ops)

def page3_stream():
    ops = []
    def txt(x, y, size, s):
        ops.append(f"BT /F1 {size} Tf 1 0 0 1 {x} {y} Tm ({esc(s)}) Tj ET")
    txt(50, 750, 14, "DECLARATION - STRESS PAGE 3")
    for i in range(12):
        txt(50, 700 - i * 24, 10, f"Line {i+1}: I hereby declare the information given is true.")
    txt(50, 380, 10, "Signature area below:")
    ops.append("0 0 0 RG 1 w 50 260 220 100 re S")
    return "\n".join(ops)

def build_pdf(path):
    streams = [page1_stream(), page2_stream(), page3_stream()]
    objects = []
    objects.append("<< /Type /Catalog /Pages 2 0 R >>")                      # 1
    objects.append("<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>")  # 2
    for i in range(3):                                                        # 3,4,5
        objects.append(
            f"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
            f"/Resources << /Font << /F1 9 0 R >> >> /Contents {6+i} 0 R >>")
    for s in streams:                                                         # 6,7,8
        objects.append(f"<< /Length {len(s.encode('latin-1'))} >>\nstream\n{s}\nendstream")
    objects.append("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")  # 9

    out = bytearray()
    out += b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n"
    offsets = [0]
    for i, body in enumerate(objects, start=1):
        offsets.append(len(out))
        out += f"{i} 0 obj\n{body}\nendobj\n".encode("latin-1")
    xref_pos = len(out)
    out += f"xref\n0 {len(objects)+1}\n".encode()
    out += b"0000000000 65535 f \n"
    for off in offsets[1:]:
        out += f"{off:010d} 00000 n \n".encode()
    out += f"trailer\n<< /Size {len(objects)+1} /Root 1 0 R >>\nstartxref\n{xref_pos}\n%%EOF\n".encode()
    with open(path, "wb") as f:
        f.write(bytes(out))
    print(f"wrote {path} ({len(out)} bytes)")

if __name__ == "__main__":
    build_pdf(sys.argv[1])
