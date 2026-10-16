"""
Writes the two SYNTHETIC depository-charges sample sheets that the spreadsheet reader tests read (P2-17, ADR 0029):

    ../dp-charges-synthetic.xls    a legacy BIFF8 workbook inside a compound document, written here from nothing
    ../dp-charges-synthetic.xlsx   the same rows as an Office Open XML workbook

Everything in them is invented (names, ISINs, amounts, the client code); nothing comes from a real download. Run it from anywhere:

    python3 -I make_dp_samples.py

The writer is minimal on purpose: one sheet, shared strings for text, numbers, one real date cell with a date format, no formulas, no styles beyond what a reader needs.
"""
import datetime
import io
import os
import struct
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.dirname(HERE)

# (text or number or date) cells; None is an empty cell.
ROWS = [
    ["DP Charges | From 01-April-2026 to 15-October-2026", None, None, "Name", "Sample Holder", "UCC", "1000000001"],
    [None],
    ["Sr.", "Date", "ISIN", "Security Name", None, None, None, "Quantity", "Buy/Sell", "Type of Transaction", None, "Charges"],
    [1.0, "04-Jun-2026", "INE245A01021", "  THE EXAMPLE POWER COMPANY LIMITED", None, None, None, 11.0, " Sell", " Other Fees", None, 12.5],
    [2.0, datetime.date(2026, 6, 5), "INE245A01021", "THE EXAMPLE POWER COMPANY LIMITED", None, None, None, 4.0, "Sell", "DP Fees", None, 0.0],
    [3.0, "09-Jun-2026", "INE555B01015", "ANOTHER EXAMPLE LIMITED", None, None, None, 7.0, "Buy", "Off-Market Fees", None, 0.0],
    ["Total", None, None, None, None, None, None, None, None, None, None, 12.5],
    ["This is a system generated report and does not require signature"],
    ["Generated on 15-10-2026"],
]


def serial(day):
    return float((day - datetime.date(1899, 12, 30)).days)


# ---------------------------------------------------------------- BIFF8 in a compound document ----------------------------------------------------------

def record(rid, data=b""):
    return struct.pack("<HH", rid, len(data)) + data


def xf(ifmt):
    # ifnt, ifmt, flags (cell xf, parent 0), alignment, rotation, indent, used-attributes, border, border, pattern
    return record(0x00E0, struct.pack("<HHHBBBBIIH", 0, ifmt, 0x0001, 0, 0, 0, 0, 0, 0, 0))


def build_biff():
    strings = []

    def sst(text):
        if text not in strings:
            strings.append(text)
        return strings.index(text)

    cells = []  # (row, col, kind, value)
    for r, row in enumerate(ROWS):
        for c, value in enumerate(row):
            if value is None:
                continue
            if isinstance(value, str):
                cells.append((r, c, "s", sst(value)))
            elif isinstance(value, datetime.date):
                cells.append((r, c, "d", serial(value)))
            else:
                cells.append((r, c, "n", float(value)))

    def sst_record():
        body = struct.pack("<II", len(strings), len(strings))
        for text in strings:
            wide = any(ord(ch) > 255 for ch in text)
            raw = text.encode("utf-16-le") if wide else text.encode("latin-1")
            body += struct.pack("<HB", len(text), 1 if wide else 0) + raw
        return record(0x00FC, body)

    def sheet_stream():
        out = record(0x0809, struct.pack("<HHHHII", 0x0600, 0x0010, 0x0DBB, 0x07CC, 0, 6))
        out += record(0x0200, struct.pack("<IIHHH", 0, len(ROWS), 0, 12, 0))
        for r, c, kind, value in sorted(cells):
            if kind == "s":
                out += record(0x00FD, struct.pack("<HHHI", r, c, 15, value))
            elif kind == "d":
                out += record(0x0203, struct.pack("<HHHd", r, c, 16, value))
            else:
                out += record(0x0203, struct.pack("<HHHd", r, c, 15, value))
        return out + record(0x000A)

    def globals_stream(sheet_offset):
        out = record(0x0809, struct.pack("<HHHHII", 0x0600, 0x0005, 0x0DBB, 0x07CC, 0, 6))
        out += record(0x0042, struct.pack("<H", 0x04B0))
        out += record(0x0031, struct.pack("<HHHHHBBBB", 200, 0, 0x7FFF, 400, 0, 0, 0, 0, 0) + bytes([5, 0]) + b"Arial")  # one FONT
        for _ in range(15):
            out += xf(0)  # the style xfs
        out += xf(0)  # 15: the general cell xf
        out += xf(14)  # 16: a cell xf with the date format m/d/yyyy
        name = b"Sheet1"
        out += record(0x0085, struct.pack("<IHBB", sheet_offset, 0, len(name), 0) + name)
        out += sst_record()
        return out + record(0x000A)

    probe = globals_stream(0)
    book = globals_stream(len(probe)) + sheet_stream()
    return book


def compound_document(stream_name, stream):
    sector = 512
    end, free, fat_sector = 0xFFFFFFFE, 0xFFFFFFFF, 0xFFFFFFFD
    # a stream below 4096 bytes would have to live in the mini stream; the workbook is padded after its last record instead (readers stop at the record end)
    padded = stream + b"\x00" * max(0, 4096 - len(stream))
    padded += b"\x00" * (-len(padded) % sector)
    data_sectors = len(padded) // sector
    dir_sector = data_sectors
    fat_index = data_sectors + 1
    fat = [i + 1 for i in range(data_sectors - 1)] + [end, end, fat_sector]
    fat += [free] * (128 - len(fat))

    def entry(name, kind, left, right, child, start, size):
        raw = name.encode("utf-16-le")
        return (raw + b"\x00" * (64 - len(raw)) + struct.pack("<H", len(raw) + 2 if name else 0) + struct.pack("<BBIII", kind, 1 if name else 0, left, right, child)
                + b"\x00" * 16 + struct.pack("<I", 0) + b"\x00" * 16 + struct.pack("<IIi", start, size, 0))

    directory = entry("Root Entry", 5, free, free, 1, end, 0) + entry(stream_name, 2, free, free, free, 0, len(padded)) + entry("", 0, free, free, free, 0, 0) * 2
    header = bytes.fromhex("D0CF11E0A1B11AE1") + b"\x00" * 16 + struct.pack("<HHHHH", 0x003E, 0x0003, 0xFFFE, 9, 6) + b"\x00" * 6
    header += struct.pack("<IIIIIIIII", 0, 1, dir_sector, 0, 4096, end, 0, end, 0)
    header += struct.pack("<I", fat_index) + struct.pack("<I", free) * 108
    assert len(header) == 512, len(header)
    return header + padded + directory + struct.pack("<128I", *fat)


# ----------------------------------------------------------------------------------------------------------------- Office Open XML -----------------------

def column(c):
    return "ABCDEFGHIJKLMNOPQRSTUVWXYZ"[c]


def build_xlsx():
    sheet = ['<?xml version="1.0" encoding="UTF-8" standalone="yes"?>', '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>']
    for r, row in enumerate(ROWS, start=1):
        sheet.append(f'<row r="{r}">')
        for c, value in enumerate(row):
            if value is None:
                continue
            ref = f"{column(c)}{r}"
            if isinstance(value, str):
                text = value.replace("&", "&amp;").replace("<", "&lt;")
                sheet.append(f'<c r="{ref}" t="inlineStr"><is><t xml:space="preserve">{text}</t></is></c>')
            elif isinstance(value, datetime.date):
                sheet.append(f'<c r="{ref}" s="1"><v>{int(serial(value))}</v></c>')
            else:
                sheet.append(f'<c r="{ref}"><v>{value:g}</v></c>')
        sheet.append("</row>")
    sheet.append("</sheetData></worksheet>")
    parts = {
        "[Content_Types].xml": '<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>',
        "_rels/.rels": '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>',
        "xl/workbook.xml": '<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>',
        "xl/_rels/workbook.xml.rels": '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>',
        "xl/styles.xml": '<?xml version="1.0" encoding="UTF-8"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="1"><font><sz val="11"/><name val="Arial"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>',
        "xl/worksheets/sheet1.xml": "".join(sheet),
    }
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, content in parts.items():
            archive.writestr(zipfile.ZipInfo(name, date_time=(2026, 10, 15, 0, 0, 0)), content)
    return buffer.getvalue()


if __name__ == "__main__":
    with open(os.path.join(OUT, "dp-charges-synthetic.xls"), "wb") as f:
        f.write(compound_document("Workbook", build_biff()))
    with open(os.path.join(OUT, "dp-charges-synthetic.xlsx"), "wb") as f:
        f.write(build_xlsx())
    print("written")
