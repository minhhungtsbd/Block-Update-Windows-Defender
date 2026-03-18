using System;
using System.IO;
using System.Xml.Serialization;
using BlockUpdateWindowsDefender.Models;

namespace BlockUpdateWindowsDefender.Services
{
    public class SettingsService
    {
        private readonly string _settingsFilePath;

        public SettingsService()
        {
            var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlockUpdateWindowsDefender");
            Directory.CreateDirectory(appFolder);
            _settingsFilePath = Path.Combine(appFolder, "settings.xml");
        }

        public AppSettings Load()
        {
            if (!File.Exists(_settingsFilePath))
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            try
            {
                var serializer = new XmlSerializer(typeof(AppSettings));
                using (var stream = File.OpenRead(_settingsFilePath))
                {
                    return (AppSettings)serializer.Deserialize(stream);
                }
            }
            catch
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }
        }

        public void Save(AppSettings settings)
        {
            var serializer = new XmlSerializer(typeof(AppSettings));
            using (var stream = File.Create(_settingsFilePath))
            {
                serializer.Serialize(stream, settings);
            }
        }
    }
}
