using Microsoft.Extensions.Hosting;
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
using EczaneScraper.Models;

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
                // Eczane klasörü yoksa oluştur
                string directoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "eczane");
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                    _logger.LogInformation("Eczane klasörü oluşturuldu");
                }

                // Dosya yoksa oluştur
                string fileName = $"{il.ToUpper()}_{tarih.Replace("/", "")}.json";
                string filePath = Path.Combine(directoryPath, fileName);
                if (!File.Exists(filePath))
                {
                    _logger.LogInformation($"{fileName} isimli dosya oluşturulmaya başlandı...");

                    // e-Devlet nöbetçi eczane sayfasına git
                    driver.Navigate().GoToUrl("https://www.turkiye.gov.tr/saglik-titck-nobetci-eczane-sorgulama");
                    _logger.LogInformation($"{il} ili için {tarih} tarihinde e-Devlet sayfasına gidildi");

                    // Sayfanın yüklenmesini bekle
                    WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
                    wait.Until(d => d.FindElement(By.Id("plakaKodu")));
                    _logger.LogInformation("Sayfa yüklendi, şehir seçiliyor...");

                    // İl seçimini yap
                    var ilSelect = new SelectElement(driver.FindElement(By.Id("plakaKodu")));
                    ilSelect.SelectByText(il);
                    _logger.LogInformation($"{il} şehri seçildi");

                    // Tarih seçimini yap
                    wait.Until(d => d.FindElement(By.Id("nobetTarihi")));
                    var dateInput = driver.FindElement(By.Id("nobetTarihi"));
                    dateInput.Clear();
                    dateInput.SendKeys(tarih);
                    _logger.LogInformation($"{tarih} tarihi seçildi");

                    // Sorgula butonuna tıkla
                    var searchButton = driver.FindElement(By.CssSelector("input[type='submit'][value='Sorgula']"));
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", searchButton);
                    _logger.LogInformation("Sorgula butonuna tıklandı");

                    // Sonuçların yüklenmesini bekle
                    try
                    {
                        wait.Until(d => d.FindElement(By.CssSelector("table, .warningContainer")));
                        _logger.LogInformation("Sonuçlar yüklendi");
                    }
                    catch (WebDriverTimeoutException)
                    {
                        _logger.LogWarning($"{il} ili için sonuçlar yüklenemedi.");
                        await File.WriteAllTextAsync(filePath, "[]");
                        return;
                    }

                    // Nöbetçi eczane bilgilerini çek
                    var warningElement = driver.FindElements(By.CssSelector(".warningContainer"));
                    List<Eczane> pharmacies = new List<Eczane>();

                    if (warningElement.Count == 0)
                    {
                        var eczaneler = driver.FindElements(By.CssSelector("table tbody tr"));

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
                                _logger.LogInformation("Harita sayfasına gidildi ve konum koordinatları alındı");

                                // Harita sayfasındaki bilgileri al
                                var latObject = ((IJavaScriptExecutor)driver).ExecuteScript("return latti;");
                                var lngObject = ((IJavaScriptExecutor)driver).ExecuteScript("return longi;");
                                double lat = Convert.ToDouble(latObject);
                                double lng = Convert.ToDouble(lngObject);

                                // Detay bilgilerini al
                                string name = driver.FindElement(By.XPath("//dt[contains(text(), 'Adı')]/following-sibling::dd")).Text;
                                string phone = driver.FindElement(By.XPath("//dt[contains(text(), 'Telefon Numarası')]/following-sibling::dd")).Text;
                                string address = driver.FindElement(By.XPath("//dt[contains(text(), 'Adresi')]/following-sibling::dd")).Text;
                                _logger.LogInformation($"Eczane bilgileri alındı: {name}");

                                pharmacies.Add(new Eczane
                                {
                                    Name = name,
                                    District = il,
                                    Phone = phone,
                                    Address = address,
                                    Latitude = lat.ToString(),
                                    Longitude = lng.ToString(),
                                    Date = tarih
                                });

                                driver.Navigate().Back();
                                wait.Until(d => d.FindElement(By.CssSelector("table tbody")));
                            }
                            catch (NoSuchElementException ex)
                            {
                                _logger.LogWarning($"Element bulunamadı: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"{il} ili için nöbetçi eczane bulunamadı.");
                    }

                    // JSON olarak kaydet
                    string json = JsonConvert.SerializeObject(pharmacies, Formatting.Indented);
                    await File.WriteAllTextAsync(filePath, json);
                    _logger.LogInformation($"{il} için nöbetçi eczane bilgileri JSON olarak kaydedildi: {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{il} ili için {tarih} tarihinde veri çekme hatası: {ex.Message}");
            }
        }
    }
}
