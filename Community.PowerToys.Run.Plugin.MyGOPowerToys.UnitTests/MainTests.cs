using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Community.PowerToys.Run.Plugin.MyGOPowerToys; // Adjust namespace if needed
using Wox.Plugin;
using Microsoft.PowerToys.Settings.UI.Library;

namespace Community.PowerToys.Run.Plugin.MyGOPowerToys.UnitTests
{
    [TestClass]
    public class MainTests
    {
        private Main _subject = null!;
        private const string CacheFileName = "mygo_cache.json";

        // Prepare a test JSON cache with six entries.
        private readonly string testJson = JsonSerializer.Serialize(new
        {
            urls = new[]
            {
                new { url = "https://drive.miyago9267.com/d/file/img/mygo/妳誤會了.jpg", alt = "妳誤會了" },
                new { url = "https://drive.miyago9267.com/d/file/img/mygo/我也一樣.jpg", alt = "我也一樣" },
                new { url = "https://drive.miyago9267.com/d/file/img/mygo/因為我很想要嘛.jpg", alt = "因為我很想要嘛" },
                new { url = "https://drive.miyago9267.com/d/file/img/mygo/測試1.jpg", alt = "測試1" },
                new { url = "https://drive.miyago9267.com/d/file/img/mygo/測試2.jpg", alt = "測試2" },
                new { url = "https://drive.miyago9267.com/d/file/img/mygo/測試3.jpg", alt = "測試3" }
            }
        });

        [TestInitialize]
        public void TestInitialize()
        {
            // Write the test JSON to the expected cache file location.
            string cachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CacheFileName);
            File.WriteAllText(cachePath, testJson);
            // Ensure the file’s timestamp is current so that the cache is not refreshed.
            File.SetLastWriteTime(cachePath, DateTime.Now);

            _subject = new Main();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            // Clean up the temporary cache file.
            string cachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CacheFileName);
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }
        }

        [TestMethod]
        public void Query_ShouldReturnFilteredResultsBasedOnAlt()
        {
            // When searching for "誤會", only the image whose alt contains this text should be returned.
            Query query = new Query("誤會");
            var results = _subject.Query(query);

            // Expect one image result (with title "妳誤會了") and no pagination items.
            Assert.IsTrue(results.Any(r => r.Title == "妳誤會了"), "Expected to find the image with alt '妳誤會了'.");
            Assert.IsFalse(results.Any(r => r.Title == "Next Page" || r.Title == "Previous Page"),
                "Did not expect any pagination results when matching items fit on one page.");
        }

        [TestMethod]
        public void Query_ShouldReturnPagination_WhenMoreThanFiveResults()
        {
            // Searching with an empty string returns all images. Our cache has 6 entries.
            Query query = new Query("");
            var results = _subject.Query(query);

            // Page size is 5. On the first page (page index 0) we expect 5 image results plus a "Next Page" result.
            Assert.AreEqual(6, results.Count, "Expected 5 image results plus a 'Next Page' navigation item.");
            Assert.IsFalse(results.Any(r => r.Title == "Previous Page"), "Did not expect a 'Previous Page' result on the first page.");
            Assert.IsTrue(results.Any(r => r.Title == "Next Page"), "Expected a 'Next Page' navigation item.");
        }

        [TestMethod]
        public void Query_ShouldReturnPreviousPageResult_WhenNotFirstPage()
        {
            // Searching with a page token for a non-first page should include a "Previous Page" option.
            // In our test cache, searching for "測試" yields three matches ("測試1", "測試2", "測試3") all on page 0.
            // Requesting page 1 (which would be empty for images) should return a "Previous Page" result.
            Query query = new Query("測試 #page=1");
            var results = _subject.Query(query);

            Assert.AreEqual(1, results.Count, "Expected only a 'Previous Page' result when no image results exist for the requested page.");
            Assert.AreEqual("Previous Page", results[0].Title);
        }

        [TestMethod]
        public void LoadContextMenus_ShouldReturnCopyImageUrlButton()
        {
            // Create a dummy result whose ContextData is an image URL.
            Result result = new Result { ContextData = "https://drive.miyago9267.com/d/file/img/mygo/妳誤會了.jpg" };
            var menus = _subject.LoadContextMenus(result);
            Assert.AreEqual(1, menus.Count, "Expected one context menu item.");
            Assert.AreEqual("Copy Image URL", menus[0].Title, "Context menu title should be 'Copy Image URL'.");
        }

        [TestMethod]
        public void AdditionalOptions_ShouldBeEmpty()
        {
            // The new implementation does not use additional options.
            var options = _subject.AdditionalOptions;
            Assert.IsFalse(options.Any(), "Expected no additional options.");
        }

        [TestMethod]
        public void UpdateSettings_ShouldNotThrowException()
        {
            // Since UpdateSettings does nothing in the new plugin, verify that it does not throw.
            PowerLauncherPluginSettings settings = new PowerLauncherPluginSettings { AdditionalOptions = new PluginAdditionalOption[0] };
            _subject.UpdateSettings(settings);
        }
    }
}
