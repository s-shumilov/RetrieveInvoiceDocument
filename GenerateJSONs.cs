using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UiPath.CodedWorkflows;


/*

Formats country specific
USA → +1 (AAA) XXX-XXXX
Singapore → +65 XXXX XXXX
Australia → +61 X XXXX XXXX
New Zealand → +64 X XXX XXXX
Romania → +40 7XX XXX XXX (mobile-style format)
Germany → +49 (0)XX XXXX XXXX

Cultures
USA → en-US
Singapore → en-SG
Australia → en-AU
New Zealand → en-NZ
Romania → ro-RO
Germany → de-DE
*/

namespace RetrieveInvoiceDocument
{
    // Data model for vendors
    public class Vendor
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

    // Data model for products
    public class Product
    {
        public string Name { get; set; }
        public int Qty { get; set; }       //base quantity - 1s 10s 100s etc
        public decimal Price { get; set; }    // price per base quantity, i.e some products are sold $1 per 100 pieces, and some $100 for 1 piece.
        public string Category { get; set; }
        public List<string> Variations { get; set; }    // adjectives that can be added to name in Invoice
        public string Alternative { get; set; }         // alternative names that can be used in Invoice
    }

    // Invoice item structure
    public class InvoiceItem
    {
        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("quantity")]
        public int Quantity { get; set; }

        [JsonProperty("unitPrice")]
        public decimal UnitPrice { get; set; }

        [JsonProperty("lineTotal")]
        public decimal LineTotal { get; set; }
    }

    // Line item data for sharing between invoice and purchase order
    public class LineItemData
    {
        public Product Product { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }

    public class GenerateJSONs : CodedWorkflow
    {
        private Random random = new Random();

        [Workflow]
        public void Execute(
            int in_LinesQuantity = 4,
            int in_FailureProbability = 0,
            string in_InvoiceID="001",
            string in_POID="001")
        {
            try
            {
                // Initialize vendor and product data
                var vendors = GetVendorData();
                var products = GetProductData();

                // Generate line items based on input quantity
                var lineItems = SelectLineItems(products, in_LinesQuantity);
                
                // Generate invoice with potential discrepancies (first to get seller/buyer info)
                var invoiceJson = GenerateInvoice(vendors, products, lineItems, in_FailureProbability, in_InvoiceID, in_POID);
                
                // Extract seller, buyer, currency, and tax name from invoice
                var seller = invoiceJson["seller"];
                var buyer = invoiceJson["buyer"];
                var currency = invoiceJson["currency"].ToString();
                var taxName = invoiceJson["taxName"].ToString();
                
                // Generate purchase order with same seller/buyer and tax info, but recalculated amounts based on original line items
                var poJson = GeneratePurchaseOrder(lineItems, seller, buyer, currency, taxName, in_POID);
                
                // Save files
                SaveInvoiceToFile(invoiceJson);
                SavePurchaseOrderToFile(poJson);
            }
            catch (Exception ex)
            {
                throw new Exception($"ERROR: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize vendor data from companies.csv file
        /// </summary>
        private List<Vendor> GetVendorData()
        {
            var vendors = new List<Vendor>();
            string csvPath = "SampleData/companies.csv";

            try
            {
                var lines = File.ReadAllLines(csvPath);
                // Skip header row
                for (int i = 1; i < lines.Length; i++)
                {
                    var values = ParseCsvLine(lines[i]);
                    if (values.Count < 9) continue; // Skip incomplete rows

                    vendors.Add(new Vendor
                    {
                        Name = values[0].Trim(),
                        Address = values[1].Trim(),
                        Country = values[2].Trim(),
                        ContactName = values[3].Trim(),
                        ContactPhone = values[4].Trim(),
                        Currency = values[5].Trim(),
                        TaxCode = values[6].Trim(),
                        TaxName = values[7].Trim(),
                        TaxRate = decimal.Parse(values[8].Trim()) // Already in percentage format
                    });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error reading vendors from CSV: {ex.Message}");
            }

            return vendors;
        }

        /// <summary>
        /// Initialize product data from products.csv file
        /// </summary>
        private List<Product> GetProductData()
        {
            var products = new List<Product>();
            string csvPath = "SampleData/products.csv";

            try
            {
                var lines = File.ReadAllLines(csvPath);
                // Skip header row
                for (int i = 1; i < lines.Length; i++)
                {
                    var values = ParseCsvLine(lines[i]);
                    if (values.Count < 6) continue; // Skip incomplete rows

                    // Parse variations from comma-separated string
                    var variations = values[4]
                        .Split(',')
                        .Select(v => v.Trim())
                        .Where(v => !string.IsNullOrEmpty(v))
                        .ToList();

                    products.Add(new Product
                    {
                        Name = values[0].Trim(),
                        Qty = int.Parse(values[1].Trim()),
                        Price = decimal.Parse(values[2].Trim()),
                        Category = values[3].Trim(),
                        Variations = variations,
                        Alternative = values[5].Trim()
                    });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error reading products from CSV: {ex.Message}");
            }

            return products;
        }

        /// <summary>
        /// Parse a CSV line handling quoted fields
        /// </summary>
        private List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var currentField = new System.Text.StringBuilder();
            bool insideQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    insideQuotes = !insideQuotes;
                }
                else if (c == ',' && !insideQuotes)
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            result.Add(currentField.ToString());
            return result;
        }

        /// <summary>
        /// Select random line items with quantities based on input parameter
        /// </summary>
        private List<LineItemData> SelectLineItems(List<Product> products, int linesQuantity)
        {
            var lineItems = new List<LineItemData>();
            var selectedProducts = new List<Product>();
            Log($"Lines Quantity {linesQuantity}");

            // Select random products without duplicates, up to the requested quantity
            var availableProducts = new List<Product>(products);
            for (int i = 0; i < linesQuantity && availableProducts.Count > 0; i++)
            {
                int index = random.Next(availableProducts.Count);
                selectedProducts.Add(availableProducts[index]);
                availableProducts.RemoveAt(index);
            }

            foreach (var product in selectedProducts)
            {
                // Random quantity between 1 and available qty
                int quantity = random.Next(1, product.Qty + 1);

                lineItems.Add(new LineItemData
                {
                    Product = product,
                    Quantity = quantity,
                    Price = product.Price
                });
            }

            return lineItems;
        }

        /// <summary>
        /// Generate an invoice with random vendor, products, and variations, plus potential discrepancies
        /// </summary>
        private JObject GenerateInvoice(List<Vendor> vendors, List<Product> products, List<LineItemData> lineItems, int failureProbability, string invoiceID, string POID)
        {
            // Pick random vendor as seller
            var seller = vendors[random.Next(vendors.Count)];

            // Pick random buyer (different from seller)
            Vendor buyer = seller;
            while (buyer == seller)
            {
                buyer = vendors[random.Next(vendors.Count)];
            }

            var invoiceItems = new List<InvoiceItem>();
            decimal subtotal = 0;

            // First pass: add all normal line items with variations
            foreach (var lineItem in lineItems)
            {
                var product = lineItem.Product;

                // Randomly decide whether to use alternative name
                string productName = random.Next(4) == 0 
                    ? product.Name 
                    : product.Alternative;

                // Add one random variation as adjective
                string variation = product.Variations[random.Next(product.Variations.Count)];
                //string description = $"{variation.First().ToString().ToUpper() + variation.Substring(1)} {productName}";
                
                string description = random.Next(4) == 0
                    ? $"{variation.First().ToString().ToUpper() + variation.Substring(1)} {productName}"
                    : $"{productName}";
                
                decimal lineTotal = lineItem.Quantity * lineItem.Price;

                invoiceItems.Add(new InvoiceItem
                {
                    Description = description,
                    Quantity = lineItem.Quantity,
                    UnitPrice = lineItem.Price,
                    LineTotal = lineTotal
                });

                subtotal += lineTotal;
            }

            // Check for discrepancy probability once at invoice level
            if (failureProbability == 0)
            {
                failureProbability = 80;
            }

            Log($"Failure probability is {failureProbability}");

            if (failureProbability > 0 && random.Next(100) < failureProbability)
            {
                int discrepancyType = random.Next(3); // 0: extra line, 1: split line, 2: price change

                switch (discrepancyType)
                {
                    case 0: // Add extra line item
                        var extraProduct = products[random.Next(products.Count)];
                        int extraQty = random.Next(1, extraProduct.Qty + 1);
                        decimal extraLineTotal = extraQty * extraProduct.Price;

                        string extraProductName = random.Next(2) == 0 ? extraProduct.Name : extraProduct.Alternative;
                        string extraVariation = extraProduct.Variations[random.Next(extraProduct.Variations.Count)];
                        string extraDescription = $"{extraVariation.First().ToString().ToUpper() + extraVariation.Substring(1)} {extraProductName}";

                        invoiceItems.Add(new InvoiceItem
                        {
                            Description = extraDescription,
                            Quantity = extraQty,
                            UnitPrice = extraProduct.Price,
                            LineTotal = extraLineTotal
                        });

                        subtotal += extraLineTotal;
                        Log($"[DISCREPANCY] Added extra line item: {extraDescription} (Qty: {extraQty})");
                        break;

                    case 1: // Split line into 2 (only if quantity > 1)
                        // Find a line item with quantity > 1
                        var splitCandidates = invoiceItems.Where(item => item.Quantity > 1).ToList();
                        if (splitCandidates.Count > 0)
                        {
                            var itemToSplit = splitCandidates[random.Next(splitCandidates.Count)];
                            int originalIndex = invoiceItems.IndexOf(itemToSplit);

                            int split1Qty = itemToSplit.Quantity / 2;
                            int split2Qty = itemToSplit.Quantity - split1Qty;
                            decimal split1Total = split1Qty * itemToSplit.UnitPrice;
                            decimal split2Total = split2Qty * itemToSplit.UnitPrice;

                            // Remove original item from subtotal and add split items
                            subtotal -= itemToSplit.LineTotal;

                            // Replace the original item with two split items
                            invoiceItems.RemoveAt(originalIndex);
                            invoiceItems.Insert(originalIndex, new InvoiceItem
                            {
                                Description = itemToSplit.Description,
                                Quantity = split1Qty,
                                UnitPrice = itemToSplit.UnitPrice,
                                LineTotal = split1Total
                            });
                            invoiceItems.Insert(originalIndex + 1, new InvoiceItem
                            {
                                Description = itemToSplit.Description,
                                Quantity = split2Qty,
                                UnitPrice = itemToSplit.UnitPrice,
                                LineTotal = split2Total
                            });

                            subtotal += split1Total + split2Total;
                            Log($"[DISCREPANCY] Split line item: {itemToSplit.Description} (Qty: {split1Qty} + {split2Qty})");
                        }
                        break;

                    case 2: // Change price by 20%
                        // Select a random line item to change price
                        if (invoiceItems.Count > 0)
                        {
                            var itemToChange = invoiceItems[random.Next(invoiceItems.Count)];
                            int changeIndex = invoiceItems.IndexOf(itemToChange);

                            decimal priceChange = random.Next(2) == 0 ? 1.20m : 0.80m; // 20% increase or decrease
                            decimal newPrice = Math.Round(itemToChange.UnitPrice * priceChange, 2);
                            decimal newLineTotal = itemToChange.Quantity * newPrice;

                            subtotal -= itemToChange.LineTotal;

                            invoiceItems[changeIndex] = new InvoiceItem
                            {
                                Description = itemToChange.Description,
                                Quantity = itemToChange.Quantity,
                                UnitPrice = newPrice,
                                LineTotal = newLineTotal
                            };

                            subtotal += newLineTotal;
                            decimal pricePercent = (priceChange - 1) * 100;
                            Log($"[DISCREPANCY] Price changed for {itemToChange.Description}: {itemToChange.UnitPrice} -> {newPrice} ({pricePercent:+0.0;-0.0}%)");
                        }
                        break;
                }
            }

            decimal taxTotal = subtotal * (seller.TaxRate / 100);
            decimal total = subtotal + taxTotal;

            var invoice = new JObject
            {
                { "invoiceNumber", $"INV-{invoiceID}" },
                { "PONumber", $"PO-{POID}" },
                { "invoiceDate", DateTime.Now.ToString("yyyy-MM-dd") },
                { "dueDate", DateTime.Now.AddDays(30).ToString("yyyy-MM-dd") },
                { "currency", seller.Currency },
                {
                    "seller", new JObject
                    {
                        { "name", seller.Name },
                        { "address", seller.Address },
                        { "country", seller.Country },
                        { "taxCode", seller.TaxCode },
                        { "ContactName", seller.ContactName },
                        { "ContactPhone", seller.ContactPhone }
                    }
                },
                {
                    "buyer", new JObject
                    {
                        { "name", buyer.Name },
                        { "address", buyer.Address },
                        { "country", buyer.Country },
                        { "taxCode", buyer.TaxCode },
                        { "ContactName", buyer.ContactName },
                        { "ContactPhone", buyer.ContactPhone }
                    }
                },
                {
                    "items", JArray.FromObject(invoiceItems)
                },
                { "subtotal", Math.Round(subtotal, 2) },
                { "taxTotal", Math.Round(taxTotal, 2) },
                { "taxName", $"{seller.TaxName} {seller.TaxRate}%" },
                { "total", Math.Round(total, 2) },
                { "notes", "Payment due within 30 days. Thank you for your business." }
            };

            return invoice;
        }

        /// <summary>
        /// Generate a purchase order with plain product names (no variations/alternatives)
        /// </summary>
        private JObject GeneratePurchaseOrder(List<LineItemData> lineItems, JToken seller, JToken buyer, string currency, string taxName, string POID)
        {
            var poItems = new List<InvoiceItem>();
            decimal subtotal = 0;

            foreach (var lineItem in lineItems)
            {
                var product = lineItem.Product;
                string description = product.Name; // Plain product name without variations or alternatives
                decimal lineTotal = lineItem.Quantity * lineItem.Price;

                poItems.Add(new InvoiceItem
                {
                    Description = description,
                    Quantity = lineItem.Quantity,
                    UnitPrice = lineItem.Price,
                    LineTotal = lineTotal
                });

                subtotal += lineTotal;
            }

            // Extract tax rate from the seller's tax name string (e.g., "VAT (19%)" -> 19)
            decimal taxRate = ExtractTaxRate(taxName);
            decimal taxTotal = subtotal * (taxRate / 100);
            decimal total = subtotal + taxTotal;

            var purchaseOrder = new JObject
            {
                { "PONumber", $"PO-{POID}" },
                { "PODate", DateTime.Now.AddDays(random.Next(30)-25).ToString("yyyy-MM-dd") },
                { "currency", currency },
                {
                    "seller", seller
                },
                {
                    "buyer", buyer
                },
                {
                    "items", JArray.FromObject(poItems)
                },
                { "subtotal", Math.Round(subtotal, 2) },
                { "taxTotal", Math.Round(taxTotal, 2) },
                { "taxName", taxName },
                { "total", Math.Round(total, 2) },
                { "notes", "Please confirm receipt and delivery date." }
            };

            return purchaseOrder;
        }

        /// <summary>
        /// Extract tax rate from tax name string format "TAX_NAME (TAX_RATE%)"
        /// </summary>
        private decimal ExtractTaxRate(string taxName)
        {
            var match = System.Text.RegularExpressions.Regex.Match(taxName, @"(\d+(?:\.\d+)?)%");
            if (match.Success && decimal.TryParse(match.Groups[1].Value, out decimal rate))
            {
                return rate;
            }
            return 0;
        }

        /// <summary>
        /// Save invoice JSON to file in the SampleData folder
        /// </summary>
        private void SaveInvoiceToFile(JObject invoice)
        {
            try
            {
                string fileName = "invoice-generated.json";
                string jsonContent = JsonConvert.SerializeObject(invoice, Formatting.Indented);

                File.WriteAllText(fileName, jsonContent);
            }
            catch (Exception ex)
            {
                // Log error or handle as needed
                throw new Exception($"Error saving invoice file: {ex.Message}");
            }
        }

        /// <summary>
        /// Save purchase order JSON to file in the SampleData folder
        /// </summary>
        private void SavePurchaseOrderToFile(JObject purchaseOrder)
        {
            try
            {
                string fileName = "purchase_order-generated.json";
                string jsonContent = JsonConvert.SerializeObject(purchaseOrder, Formatting.Indented);

                File.WriteAllText(fileName, jsonContent);
            }
            catch (Exception ex)
            {
                // Log error or handle as needed
                throw new Exception($"Error saving purchase order file: {ex.Message}");
            }
        }
    }
}