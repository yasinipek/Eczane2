using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Microsoft.Extensions.Logging;

namespace WebApplication8.Services
{
    public class ScrapingService : IHostedService, IDisposable
    {
        private readonly ILogger<ScrapingService> _logger;
        private Timer _timer;

        public ScrapingService(ILogger<ScrapingService> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromHours(24));
            return Task.CompletedTask;
        }

        private void DoWork(object state)
        {
            Task.Run(async () =>
            {
                List<string> iller = new List<string> { "ANKARA", "AFYONKARAHİSAR" }; // İl listesini burada belirtin
                DateTime today = DateTime.Now;
                DateTime tomorrow = DateTime.Now.AddDays(1);

                var todayPharmacies = await ScrapeEczaneData(iller, today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                SavePharmaciesToFile(todayPharmacies, "today.json");

                var tomorrowPharmacies = await ScrapeEczaneData(iller, tomorrow.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                SavePharmaciesToFile(tomorrowPharmacies, "tomorrow.json");
            });
        }

        private async Task<List<Pharmacy>> ScrapeEczaneData(List<string> iller, string tarih)
        {
            var allPharmacies = new List<Pharmacy>();

            foreach (var il in iller)
            {
                _logger.LogInformation($"Navigating to e-Devlet page for {il} on {tarih}");

                using (IWebDriver driver = new ChromeDriver())
                {
                    driver.Navigate().GoToUrl("https://www.turkiye.gov.tr/saglik-titck-nobetci-eczane-sorgulama");

                    WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
                    wait.Until(d => d.FindElement(By.Id("plakaKodu")));

                    var ilSelect = new SelectElement(driver.FindElement(By.Id("plakaKodu")));
                    ilSelect.SelectByText(il);

                    wait.Until(d => d.FindElement(By.Id("nobetTarihi")));
                    var dateInput = driver.FindElement(By.Id("nobetTarihi"));
                    dateInput.Clear();
                    dateInput.SendKeys(tarih);

                    var searchButton = driver.FindElement(By.CssSelector("input[type='submit'][value='Sorgula']"));
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", searchButton);

                    wait.Until(d => d.FindElement(By.CssSelector("table tbody")));

                    var eczaneler = driver.FindElements(By.CssSelector("table tbody tr"));
                    var pharmacies = new List<Pharmacy>();

                    for (int i = 0; i < eczaneler.Count; i++)
                    {
                        eczaneler = driver.FindElements(By.CssSelector("table tbody tr"));
                        var eczane = eczaneler[i];

                        try
                        {
                            var locationElement = eczane.FindElement(By.CssSelector("td[data-cell-order='4'] a"));
                            var locationUrl = locationElement.GetAttribute("href");

                            driver.Navigate().GoToUrl(locationUrl);
                            wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return typeof latti !== 'undefined' && typeof longi !== 'undefined';"));

                            var lat = (double)((IJavaScriptExecutor)driver).ExecuteScript("return latti;");
                            var lng = (double)((IJavaScriptExecutor)driver).ExecuteScript("return longi;");

                            string name = driver.FindElement(By.XPath("//dt[contains(text(), 'Adı')]/following-sibling::dd")).Text;
                            string phone = driver.FindElement(By.XPath("//dt[contains(text(), 'Telefon Numarası')]/following-sibling::dd")).Text;
                            string address = driver.FindElement(By.XPath("//dt[contains(text(), 'Adresi')]/following-sibling::dd")).Text;

                            pharmacies.Add(new Pharmacy
                            {
                                Name = name,
                                District = il,
                                Phone = phone,
                                Address = address,
                                Latitude = lat.ToString(CultureInfo.InvariantCulture),
                                Longitude = lng.ToString(CultureInfo.InvariantCulture)
                            });
                        }
                        catch (NoSuchElementException ex)
                        {
                            _logger.LogWarning($"Element not found for index {i}: {ex.Message}");
                        }

                        driver.Navigate().Back();
                        wait.Until(d => d.FindElement(By.CssSelector("table tbody")));
                    }

                    allPharmacies.AddRange(pharmacies);
                }
            }

            return allPharmacies;
        }

        private void SavePharmaciesToFile(List<Pharmacy> pharmacies, string fileName)
        {
            string directoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "eczane");
            Directory.CreateDirectory(directoryPath);
            string filePath = Path.Combine(directoryPath, fileName);

            string json = JsonConvert.SerializeObject(pharmacies, Formatting.Indented);
            System.IO.File.WriteAllText(filePath, json);

            _logger.LogInformation($"Eczane bilgileri JSON olarak kaydedildi: {filePath}");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
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
