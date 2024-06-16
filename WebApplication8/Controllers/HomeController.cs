using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace EczaneScraper.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            var eczaneler = LoadPharmacies("today.json");
            return View(eczaneler);
        }

        private List<Pharmacy> LoadPharmacies(string fileName)
        {
            var directoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "eczane");
            var filePath = Path.Combine(directoryPath, fileName);
            var json = System.IO.File.ReadAllText(filePath);
            var pharmacies = JsonConvert.DeserializeObject<List<Pharmacy>>(json);
            return pharmacies;
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
