﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EczaneScraper.Services
{
    public class ScrapingService : BackgroundService
    {
        private readonly List<string> iller = new List<string>
        {
            "ADANA", "ADIYAMAN", "AFYONKARAHİSAR", "AĞRI", "AMASYA", "ANKARA", "ANTALYA", "ARTVİN", "AYDIN", "BALIKESİR",
            "BİLECİK", "BİNGÖL", "BİTLİS", "BOLU", "BURDUR", "BURSA", "ÇANAKKALE", "ÇANKIRI", "ÇORUM", "DENİZLİ",
            "DİYARBAKIR", "EDİRNE", "ELAZIĞ", "ERZİNCAN", "ERZURUM", "ESKİŞEHİR", "GAZİANTEP", "GİRESUN", "GÜMÜŞHANE",
            "HAKKARİ", "HATAY", "ISPARTA", "MERSİN", "İSTANBUL", "İZMİR", "KARS", "KASTAMONU", "KAYSERİ", "KIRKLARELİ",
            "KIRŞEHİR", "KOCAELİ", "KONYA", "KÜTAHYA", "MALATYA", "MANİSA", "KAHRAMANMARAŞ", "MARDİN", "MUĞLA", "MUŞ",
            "NEVŞEHİR", "NİĞDE", "ORDU", "RİZE", "SAKARYA", "SAMSUN", "SİİRT", "SİNOP", "SİVAS", "TEKİRDAĞ",
            "TOKAT", "TRABZON", "TUNCELİ", "ŞANLIURFA", "UŞAK", "VAN", "YOZGAT", "ZONGULDAK", "AKSARAY", "BAYBURT",
            "KARAMAN", "KIRIKKALE", "BATMAN", "ŞIRNAK", "BARTIN", "ARDAHAN", "IĞDIR", "YALOVA", "KARABÜK", "KİLİS",
            "OSMANİYE", "DÜZCE"
        };

        private readonly ILogger<ScrapingService> _logger;

        public ScrapingService(ILogger<ScrapingService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var tarih = DateTime.Now.ToString("dd'/'MM'/'yyyy", CultureInfo.InvariantCulture); // Bugünün tarihi

            foreach (var il in iller)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                using (IWebDriver driver = new ChromeDriver(@"C:\chromedriver"))
                {
                    await ScrapeEczaneData(driver, il, tarih, stoppingToken);
                }
            }
        }

        private async Task ScrapeEczaneData(IWebDriver driver, string il, string tarih, CancellationToken stoppingToken)
        {
            try
            {
                // e-Devlet nöbetçi eczane sayfasına git
                driver.Navigate().GoToUrl("https://www.turkiye.gov.tr/saglik-titck-nobetci-eczane-sorgulama");
                _logger.LogInformation($"Navigated to e-Devlet page for {il} on {tarih}");

                // Sayfanın yüklenmesini bekle
                WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
                wait.Until(d => d.FindElement(By.Id("plakaKodu")));
                _logger.LogInformation("Page loaded, selecting city...");

                // İl seçimini yap
                var ilSelect = new SelectElement(driver.FindElement(By.Id("plakaKodu")));
                ilSelect.SelectByText(il);
                _logger.LogInformation($"Selected city {il}");

                // Tarih seçimini yap
                wait.Until(d => d.FindElement(By.Id("nobetTarihi")));
                var dateInput = driver.FindElement(By.Id("nobetTarihi"));
                dateInput.Clear();
                dateInput.SendKeys(tarih);
                _logger.LogInformation($"Selected date {tarih}");

                // Sorgula butonuna tıkla
                var searchButton = driver.FindElement(By.CssSelector("input[type='submit'][value='Sorgula']"));
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", searchButton);
                _logger.LogInformation($"Clicked search button for {il} on {tarih}");

                // Sonuçların yüklenmesini bekle
                wait.Until(d => d.FindElement(By.CssSelector("table")));
                _logger.LogInformation("Results loaded");

                // Nöbetçi eczane bilgilerini çek
                var eczaneler = driver.FindElements(By.CssSelector("table tbody tr"));
                List<Pharmacy> pharmacies = new List<Pharmacy>();

                for (int i = 0; i < eczaneler.Count; i++)
                {
                    // Elementi her seferinde yeniden bul
                    eczaneler = driver.FindElements(By.CssSelector("table tbody tr"));
                    var eczane = eczaneler[i];

                    try
                    {
                        var locationElement = eczane.FindElement(By.CssSelector("td[data-cell-order='4'] a"));
                        var locationUrl = locationElement.GetAttribute("href");

                        // Harita sayfasına git ve bilgileri al
                        driver.Navigate().GoToUrl(locationUrl);
                        wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return typeof latti !== 'undefined' && typeof longi !== 'undefined';"));
                        _logger.LogInformation("Navigated to map page and got location coordinates");

                        // Harita sayfasındaki bilgileri al
                        var lat = (double)((IJavaScriptExecutor)driver).ExecuteScript("return latti;");
                        var lng = (double)((IJavaScriptExecutor)driver).ExecuteScript("return longi;");

                        // Detay bilgilerini al
                        string name = null;
                        string phone = null;
                        string address = null;
                        try
                        {
                            name = driver.FindElement(By.XPath("//dt[contains(text(), 'Adı')]/following-sibling::dd")).Text;
                            phone = driver.FindElement(By.XPath("//dt[contains(text(), 'Telefon Numarası')]/following-sibling::dd")).Text;
                            address = driver.FindElement(By.XPath("//dt[contains(text(), 'Adresi')]/following-sibling::dd")).Text;
                            _logger.LogInformation($"Fetched details for pharmacy: {name}");
                        }
                        catch (NoSuchElementException ex)
                        {
                            _logger.LogWarning($"Element not found for index {i}: {ex.Message}");
                        }

                        if (!string.IsNullOrEmpty(name))  // Eczane adının boş olmadığını kontrol et
                        {
                            pharmacies.Add(new Pharmacy
                            {
                                Name = name,
                                District = il, // İlçe bilgisi eğer harita sayfasında yoksa, Ana sayfadaki bilgilere göre düzeltilmeli
                                Phone = phone,
                                Address = address,
                                Latitude = lat.ToString(),
                                Longitude = lng.ToString(),
                            });
                        }

                        driver.Navigate().Back();
                        wait.Until(d => d.FindElement(By.CssSelector("table tbody")));
                    }
                    catch (NoSuchElementException ex)
                    {
                        _logger.LogWarning($"Element not found for index {i}: {ex.Message}");
                    }
                }

                // JSON dosya adını oluştur
                string directoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "eczane");
                Directory.CreateDirectory(directoryPath); // Klasörü oluştur
                string fileName = $"{il.ToLower()}{tarih.Replace("/", "")}.json";
                string filePath = Path.Combine(directoryPath, fileName);

                // JSON olarak kaydet
                string json = JsonConvert.SerializeObject(pharmacies, Formatting.Indented);
                await File.WriteAllTextAsync(filePath, json);
                _logger.LogInformation($"{il} için nöbetçi eczane bilgileri JSON olarak kaydedildi: {filePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error scraping data for {il} on {tarih}: {ex.Message}");
            }
        }

        public class Pharmacy
        {
            public string Name { get; set; }
            public string District { get; set; }
            public string Phone { get; set; }
            public string Address { get; set; }
            public string Latitude { get; set; }
            public string Longitude { get; set; }
        }
    }
}