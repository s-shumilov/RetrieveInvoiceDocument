using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Globalization;
using UiPath.CodedWorkflows;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Font;
using iText.Kernel.Colors;
using iText.IO.Font.Constants;
using iText.Layout.Borders;


namespace InvoicePDFGenerator
{


public class PDFWriter : RetrieveInvoiceDocument.CodedWorkflow
{
    [Workflow]
    public void Execute()
    {    
        string jsonPath = @"invoice-generated.json";
        string outputPath = @"invoice-generated.pdf";

        // Read JSON
        string jsonContent = File.ReadAllText(jsonPath);

        // Deserialize
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        Invoice invoice = JsonSerializer.Deserialize<Invoice>(jsonContent, options);

        // Create PDF document
        using (var writer = new PdfWriter(outputPath))
        using (var pdf = new PdfDocument(writer))
        {
            var document = new Document(pdf);
            document.SetMargins(0, 0, 0, 0); 
            
            PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            
                string invoiceColorText = System.IO.File.ReadAllText("invoice-color.txt");
                Color invoiceColor = WebColors.GetRGBColor(invoiceColorText);


                // Create a 2-column table for the header 
                Table headerTable = new Table(2)
                    .UseAllAvailableWidth()
                    .SetHeight(80)
                    .SetBackgroundColor(invoiceColor);

                // Seller name (left-aligned, white font) 
                Cell leftHeader = new Cell()
                    .Add(new Paragraph(invoice.Seller.Name)
                    .SetFontColor(ColorConstants.WHITE)
                    .SetFont(boldFont))
                    .SetFontSize(20)
                    .SetPadding(25)
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.LEFT)
                    .SetBackgroundColor(invoiceColor);

                // Invoice number (right-aligned, white font) 
                Cell rightHeader = new Cell()
                    .Add(new Paragraph($"Invoice #: {invoice.InvoiceNumber}")
                    .SetFontColor(ColorConstants.WHITE)
                    .SetFont(boldFont))
                    .SetFontSize(14)
                    .SetPadding(20)
                    .SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT)
                    .SetBackgroundColor(invoiceColor);

                // Add cells to table 
                headerTable.AddCell(leftHeader);
                headerTable.AddCell(rightHeader); 
            
            // Add header to document 
            document.Add(headerTable);                        
                        
            Table partyTable = new Table(2).UseAllAvailableWidth().SetFontSize(10); 
            
            // Seller column 
            Cell sellerCell = new Cell() 
                .Add(new Paragraph(invoice.Seller.Name).SetFontSize(12).SetFont(boldFont)) 
                .Add(new Paragraph(invoice.Seller.Address)) 
                .Add(new Paragraph(invoice.Seller.Country)) 
                .Add(new Paragraph($"Tax ID: {invoice.Seller.TaxCode}")) 
                .Add(new Paragraph($"Email: {invoice.Seller.ContactName}")) 
                .Add(new Paragraph($"Phone: {invoice.Seller.ContactPhone}")) 
                .SetBorder(Border.NO_BORDER) 
                .SetPadding(30);

            
            // Buyer column 
            Cell buyerCell = new Cell() 
                .Add(new Paragraph("Bill To:").SetFontSize(12).SetFont(boldFont)) 
                .Add(new Paragraph(invoice.Buyer.Name)) 
                .Add(new Paragraph(invoice.Buyer.Address)) 
                .Add(new Paragraph(invoice.Buyer.Country)) 
                .Add(new Paragraph($"Tax ID: {invoice.Buyer.TaxCode}")) 
                .SetBorder(Border.NO_BORDER)
                .SetPadding(30); 
            
            // Add cells to table 
            partyTable.AddCell(sellerCell); 
            partyTable.AddCell(buyerCell); 
            
            // Add table to document 
            document.Add(partyTable);         

            
            
            Table docTable = new Table(2).UseAllAvailableWidth().SetFontSize(10); 
            
            Cell invoiceCell = new Cell() 
                .Add(new Paragraph($"Invoice #: {invoice.InvoiceNumber}").SetFontSize(12).SetFont(boldFont))
                .Add(new Paragraph($"Invoice Date: {invoice.InvoiceDate:yyyy-MM-dd}").SetPaddingLeft(20))
                .Add(new Paragraph($"Due Date: {invoice.DueDate:yyyy-MM-dd}").SetPaddingLeft(20))
                .SetBorder(Border.NO_BORDER) 
                .SetPadding(30);

            Cell poCell = new Cell() 
                .Add(new Paragraph($"Purchase Order #: {invoice.PONumber}").SetFontSize(12).SetFont(boldFont))
                .SetBorder(Border.NO_BORDER)
                .SetPadding(30); 
            
            docTable.AddCell(invoiceCell); 
            docTable.AddCell(poCell); 
            document.Add(docTable);
            
            
            // Table for Invoice Items
            Table linesTable = new Table(4)
                .UseAllAvailableWidth()  // Full page width
                .SetBorder(Border.NO_BORDER)
                .SetFixedLayout()
                .SetMargin(30)
                .SetFontSize(10);

            // Table Header with Styling
            linesTable.AddHeaderCell(new Cell().Add(new Paragraph("Description")).SetWidth(UnitValue.CreatePercentValue(50)).SetBackgroundColor(invoiceColor).SetTextAlignment(TextAlignment.CENTER).SetPadding(5).SetFontColor(ColorConstants.WHITE));
            linesTable.AddHeaderCell(new Cell().Add(new Paragraph("Qty")).SetBackgroundColor(invoiceColor).SetTextAlignment(TextAlignment.CENTER).SetPadding(5).SetFontColor(ColorConstants.WHITE));
            linesTable.AddHeaderCell(new Cell().Add(new Paragraph("Unit Price")).SetBackgroundColor(invoiceColor).SetTextAlignment(TextAlignment.CENTER).SetPadding(5).SetFontColor(ColorConstants.WHITE));
            linesTable.AddHeaderCell(new Cell().Add(new Paragraph($"Total ({invoice.Currency})")).SetBackgroundColor(invoiceColor).SetTextAlignment(TextAlignment.CENTER).SetPadding(5).SetFontColor(ColorConstants.WHITE));

            // Determine culture for formatting
            var culture = GetCultureInfo(invoice?.Seller?.Country);

            // Table rows
            foreach (var item in invoice.Items)
            {
                linesTable.AddCell(new Cell().Add(new Paragraph(item.Description)).SetPadding(5));
                linesTable.AddCell(new Cell().Add(new Paragraph(item.Quantity.ToString())).SetPadding(5).SetTextAlignment(TextAlignment.CENTER));
                linesTable.AddCell(new Cell().Add(new Paragraph($"{item.UnitPrice.ToString("C", culture)}")).SetPadding(5).SetTextAlignment(TextAlignment.RIGHT));
                linesTable.AddCell(new Cell().Add(new Paragraph($"{item.LineTotal.ToString("C", culture)}")).SetPadding(5).SetTextAlignment(TextAlignment.RIGHT));
            }

            document.Add(linesTable);

            // Totals Section
            document.Add(new Paragraph($"Subtotal: {invoice.Subtotal.ToString("C", culture)}")
                .SetTextAlignment(TextAlignment.RIGHT)
                .SetPaddingRight(20));
            
            document.Add(new Paragraph($"{invoice.TaxName}: {invoice.TaxTotal.ToString("C", culture)}")
                .SetTextAlignment(TextAlignment.RIGHT)
                .SetPaddingRight(20));
            
            document.Add(new Paragraph($"Total ({invoice.Currency}): {invoice.Total.ToString("C", culture)}")
                .SetFontSize(14)
                .SetFont(boldFont)
                .SetTextAlignment(TextAlignment.RIGHT)
                .SetPaddingRight(20));

            // Notes Section
            document.Add(new Paragraph("Notes:").SetFontSize(12).SetPaddingLeft(20).SetPaddingTop(40));
            document.Add(new Paragraph(invoice.Notes).SetPaddingLeft(60));
            
            Table footerTable = new Table(2) 
                .UseAllAvailableWidth() 
                .SetHeight(20)
                .SetBackgroundColor(invoiceColor);
            
            footerTable.SetFixedPosition(1, 0, 0, 600);
                
            document.Add(footerTable);                        

        }
        
        Console.WriteLine("Invoice PDF generated successfully.");
    }

    private static CultureInfo GetCultureInfo(string country)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"USA", "en-US"},
            {"United States", "en-US"},
            {"United States of America", "en-US"},
            {"US", "en-US"},
            {"Singapore", "en-SG"},
            {"Australia", "en-AU"},
            {"New Zealand", "en-NZ"},
            {"Romania", "ro-RO"},
            {"Germany", "de-DE"},
            {"Deutschland", "de-DE"}
        };

        if (string.IsNullOrWhiteSpace(country))
            return new CultureInfo("en-US");

        var key = country.Trim();
        if (map.TryGetValue(key, out var cultureName))
            return new CultureInfo(cultureName);

        return new CultureInfo("en-US");
    }

}

#region Models

public class Invoice
{
    public string InvoiceNumber { get; set; }
    public string PONumber { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public string Currency { get; set; }
    public Party Seller { get; set; }
    public Party Buyer { get; set; }
    public List<InvoiceItem> Items { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public string TaxName { get; set; }
    public decimal Total { get; set; }
    public string Notes { get; set; }
}

public class Party
{
    public string Name { get; set; }
    public string Address { get; set; }
    public string Country { get; set; }
    public string ContactName { get; set; }
    public string ContactPhone { get; set; }
    public string Currency { get; set; }
    public string TaxCode { get; set; }
    public string TaxName { get; set; }
    public decimal TaxRate { get; set; }
}

public class InvoiceItem
{
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

}

#endregion
}