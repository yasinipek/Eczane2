using EczaneScraper.Models;
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
            return View();
        }

        private List<Eczane> LoadPharmacies(string fileName)
        {
            var directoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "eczane");
            var filePath = Path.Combine(directoryPath, fileName);
            var json = System.IO.File.ReadAllText(filePath);
            var pharmacies = JsonConvert.DeserializeObject<List<Eczane>>(json);
            return pharmacies;
        }
    }
}
