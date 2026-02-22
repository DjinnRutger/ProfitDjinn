"""Generate invoice PDFs using fpdf2 (pure Python, no system deps)."""
try:
    from fpdf import FPDF, XPos, YPos
    _FPDF_AVAILABLE = True
except ImportError:
    _FPDF_AVAILABLE = False

# Dark navy matching the old invoice table header
_NAV_R, _NAV_G, _NAV_B = 28, 52, 88


def generate_invoice_pdf(invoice, company: dict) -> bytes:
    """Return PDF bytes for the given Invoice ORM object."""
    if not _FPDF_AVAILABLE:
        raise ImportError("fpdf2 is required: pip install fpdf2")

    pdf = FPDF()
    pdf.add_page()
    pdf.set_margins(15, 15, 15)
    eff_w = pdf.epw
    half = eff_w * 0.55
    right_x = pdf.l_margin + half

    # ── Two-column header ─────────────────────────────────────────────────────
    top_y = pdf.get_y()

    # Left: company name
    pdf.set_xy(pdf.l_margin, top_y)
    pdf.set_font("Helvetica", "B", 18)
    pdf.set_text_color(0, 0, 0)
    pdf.cell(half, 10, company.get("company_name", ""))

    # Right: INVOICE label
    pdf.set_xy(right_x, top_y)
    pdf.set_font("Helvetica", "B", 20)
    pdf.cell(eff_w - half, 10, "INVOICE", align="R")

    # Left: address lines
    addr_lines = []
    if company.get("company_address"):
        addr_lines.append(company["company_address"])
    city_state = (
        f"{company.get('company_city', '')}, "
        f"{company.get('company_state', '')} "
        f"{company.get('company_zip', '')}"
    ).strip(", ").strip()
    if city_state:
        addr_lines.append(city_state)
    if company.get("company_email"):
        addr_lines.append(company["company_email"])
    if company.get("company_phone"):
        addr_lines.append(company["company_phone"])

    pdf.set_font("Helvetica", "", 10)
    left_y = top_y + 11
    for line in addr_lines:
        if line.strip():
            pdf.set_xy(pdf.l_margin, left_y)
            pdf.cell(half, 5, line)
            left_y += 5

    # Right: Invoice # and Date
    pdf.set_font("Helvetica", "", 10)
    pdf.set_xy(right_x, top_y + 12)
    pdf.cell(eff_w - half, 6, f"Invoice #: {invoice.invoice_number}", align="R")
    pdf.set_xy(right_x, top_y + 18)
    pdf.cell(eff_w - half, 6, f"Date: {invoice.date.strftime('%Y-%m-%d')}", align="R")

    # Move below both columns + thin rule
    pdf.set_y(max(left_y + 3, top_y + 28))
    pdf.set_draw_color(180, 180, 180)
    pdf.line(pdf.l_margin, pdf.get_y(), pdf.l_margin + eff_w, pdf.get_y())
    pdf.ln(5)

    # ── Bill To ───────────────────────────────────────────────────────────────
    pdf.set_font("Helvetica", "B", 10)
    pdf.cell(0, 6, "Bill To:", new_x=XPos.LMARGIN, new_y=YPos.NEXT)
    pdf.ln(1)

    pdf.set_font("Helvetica", "", 10)
    c = invoice.customer
    bill_lines = [c.name]
    if c.attn:
        bill_lines.append(f"Attn: {c.attn}")
    if c.address:
        bill_lines.append(c.address)
    city_line = ", ".join(p for p in [c.city, c.state, c.zip_code] if p)
    if city_line:
        bill_lines.append(city_line)
    for line in bill_lines:
        pdf.cell(0, 5.5, line, new_x=XPos.LMARGIN, new_y=YPos.NEXT)

    pdf.ln(7)

    # ── Line items table ──────────────────────────────────────────────────────
    col_num  = eff_w * 0.08
    col_desc = eff_w * 0.44
    col_qty  = eff_w * 0.11
    col_up   = eff_w * 0.18
    col_tot  = eff_w - col_num - col_desc - col_qty - col_up

    # Header row
    pdf.set_fill_color(_NAV_R, _NAV_G, _NAV_B)
    pdf.set_text_color(255, 255, 255)
    pdf.set_font("Helvetica", "B", 9)
    pdf.cell(col_num,  7.5, "Line #",      fill=True, align="C")
    pdf.cell(col_desc, 7.5, "Description", fill=True)
    pdf.cell(col_qty,  7.5, "Qty",         fill=True, align="C")
    pdf.cell(col_up,   7.5, "Unit Price",  fill=True, align="R")
    pdf.cell(col_tot,  7.5, "Total",       fill=True, align="R")
    pdf.ln()
    pdf.set_text_color(0, 0, 0)

    # Data rows
    pdf.set_font("Helvetica", "", 10)
    pdf.set_draw_color(220, 220, 220)
    for i, item in enumerate(invoice.line_items, start=1):
        bg = (249, 250, 251) if i % 2 == 0 else (255, 255, 255)
        pdf.set_fill_color(*bg)
        pdf.cell(col_num,  7, str(i),                   fill=True, align="C",
                 border="B")
        pdf.cell(col_desc, 7, item.description,         fill=True, border="B")
        pdf.cell(col_qty,  7, f"{item.quantity:g}",     fill=True, align="C",
                 border="B")
        pdf.cell(col_up,   7, f"${item.unit_price:,.2f}", fill=True, align="R",
                 border="B")
        pdf.cell(col_tot,  7, f"${item.amount:,.2f}",  fill=True, align="R",
                 border="B")
        pdf.ln()

    pdf.ln(5)

    # Total — right-aligned below table (matching old invoice style)
    pdf.set_font("Helvetica", "B", 11)
    pdf.cell(eff_w - col_up - col_tot, 7, "")
    pdf.cell(col_up, 7, "Total:", align="R")
    pdf.cell(col_tot, 7, f"${invoice.total:,.2f}", align="R")
    pdf.ln()

    pdf.ln(8)

    # ── Notes & terms ─────────────────────────────────────────────────────────
    if invoice.notes:
        pdf.set_font("Helvetica", "B", 9)
        pdf.cell(0, 5, "Notes:", new_x=XPos.LMARGIN, new_y=YPos.NEXT)
        pdf.set_font("Helvetica", "", 9)
        pdf.multi_cell(0, 5, invoice.notes)
        pdf.ln(3)

    pdf.set_font("Helvetica", "", 9)
    pdf.set_text_color(80, 80, 80)
    if invoice.term1:
        pdf.cell(0, 5, invoice.term1, new_x=XPos.LMARGIN, new_y=YPos.NEXT)
    if invoice.term2:
        pdf.cell(0, 5, invoice.term2, new_x=XPos.LMARGIN, new_y=YPos.NEXT)

    # ── Footer ────────────────────────────────────────────────────────────────
    # Disable auto page-break so positioning near bottom doesn't spill to page 2
    pdf.set_auto_page_break(False)
    pdf.set_y(pdf.h - 20)
    pdf.set_font("Helvetica", "B", 11)
    pdf.set_text_color(0, 0, 0)
    pdf.cell(0, 10, "Thank You for Your Business!", align="C")

    return bytes(pdf.output())
