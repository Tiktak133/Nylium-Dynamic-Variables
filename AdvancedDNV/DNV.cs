using System.Text;
using System.Timers;

namespace AdvancedDNV
{
    public class DNV
    {
        private DNVProperties _defaultProperties;

        private Container? _conMain;
        private Container? _metadata;
        private string Pass = string.Empty;
        private string _FilePath = string.Empty;

        internal System.Timers.Timer? autoSaveTimer;  // Auto save

        public iProperties? Properties;
        public iMeta? Meta;

        public bool isOpened { get; internal set; }
        private readonly object _savingLock = new object();

        public DNV(string filePath, string Password, int AutoSaveDelayMs)
        {
            _defaultProperties = new DNVProperties();
            this.Pass = Password;
            Initialize(filePath, AutoSaveDelayMs);
        }

        public DNV(string filePath, string Password, bool AutoSave)
        {
            _defaultProperties = new DNVProperties();
            this.Pass = Password;
            if (AutoSave)
                Initialize(filePath, _defaultProperties.AutoSaveDelayMs);
            else
                Initialize(filePath, -1);
        }

        public DNV(string filePath, string Password)
        {
            _defaultProperties = new DNVProperties();
            this.Pass = Password;
            if (_defaultProperties.AutoSaveEnable)
                Initialize(filePath, _defaultProperties.AutoSaveDelayMs);
            else
                Initialize(filePath, -1);
        }

        public DNV(string filePath, int AutoSaveDelayMs) 
        {
            _defaultProperties = new DNVProperties();
            Initialize(filePath, AutoSaveDelayMs);
        }

        public DNV(string filePath, bool AutoSave)
        {
            _defaultProperties = new DNVProperties();
            if (AutoSave)
                Initialize(filePath, _defaultProperties.AutoSaveDelayMs);
            else
                Initialize(filePath, -1);
        }

        public DNV(string filePath)
        {
            _defaultProperties = new DNVProperties();
            if (_defaultProperties.AutoSaveEnable)
                Initialize(filePath, _defaultProperties.AutoSaveDelayMs);
            else
                Initialize(filePath, -1);
        }

        // - Initialize -----------------------------------

        private void Initialize(string filePath, int AutoSaveDelayMs)
        {
            Properties = new iProperties(this);
            Meta = new iMeta(this);

            isOpened = false;
            _FilePath = filePath;

            if (AutoSaveDelayMs > 0)
                InitializeNewAutoSaveTimer(AutoSaveDelayMs);
        }

        private void InitializeNewAutoSaveTimer(int AutoSaveDelayMs)
        {
            autoSaveTimer = new System.Timers.Timer(AutoSaveDelayMs);
            autoSaveTimer.Elapsed += async (sender, e) => await TimerElapsedAsync(sender, e);
            autoSaveTimer.AutoReset = true; // Ustawienie na true, aby timer wykonywał się cyklicznie
        }

        private Task TimerElapsedAsync(object sender, ElapsedEventArgs e)
        {
            Save();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Open the DNV file and load its contents into memory.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception">Thrown when the file cannot be read, the password is incorrect, or the file version is unsupported.</exception>
        public bool Open()
        {
            lock (_savingLock)
            {
                if (!isOpened)
                {
                    if (File.Exists(_FilePath))
                    {
                        // Otwórz plik w trybie strumieniowym
                        using (FileStream fs = new FileStream(_FilePath, FileMode.Open, FileAccess.Read))
                        using (BinaryReader reader = new BinaryReader(fs))
                        {
                            // Wersja DNV
                            byte[] stringBytes = reader.ReadBytes(16); // Wczytaj dokładnie 16 bajtów
                            string text = Encoding.ASCII.GetString(stringBytes); // Zakładamy, że string jest kodowany w UTF-8

                            if (text != null && text == "DNVFile_byNylium")
                            {
                                int version = reader.ReadUInt16();
                                if (version >= _defaultProperties.MyVersion) // AdvancedDNV
                                {
                                    int metaDataCount = reader.ReadInt32();

                                    // Tworzymy nowe bufory o rozmiarze odpowiednim do ilości danych
                                    byte[] metadataData = reader.ReadBytes(metaDataCount);
                                    byte[]? mainData = null;

                                    // If metadata is empty (only 4-byte header), create empty metadata container
                                    if (metadataData == null || metadataData.Length <= 4)
                                    {
                                        _metadata = new Container("MetaData", null);
                                    }
                                    else
                                    {
                                        // Odbezpiecz Metadane
                                        new DNVEncryption().SimpleEncryptInPlace(metadataData, _defaultProperties.MetadataEncryption);

                                        // Validate metadata header and create container or empty metadata if invalid
                                        int metadataEndIndex = 0;
                                        if (metadataData.Length >= 4)
                                            metadataEndIndex = BitConverter.ToInt32(metadataData, 0);

                                        if (metadataData.Length < 4 || metadataEndIndex <= 4 || metadataData.Length < metadataEndIndex)
                                        {
                                            // Invalid or empty metadata, create empty container
                                            _metadata = new Container("MetaData", null);
                                        }
                                        else
                                        {
                                            // Inicjalizujemy obiekty `Container`
                                            _metadata = new Container(metadataData, null, new advInt32().Set(4), metadataEndIndex);
                                        }
                                        mainData = reader.ReadBytes((int)(fs.Length - fs.Position));

                                        // Odszyfrowywanie zawartości
                                        byte[] salt = _metadata["encryption"].Value("salt").Get(new byte[0]);
                                        if (salt.Length > 0)
                                        {
                                            if (Pass != string.Empty)
                                            {
                                                byte[] controlBlock = _metadata["encryption"].Value("controlBlock").Get(new byte[0]);
                                                short securityLevel = _metadata["encryption"].Value("securityLevel").Get();
                                                if (controlBlock.Length > 0)
                                                {
                                                    byte[] Key = null; byte[] IV = null;
                                                    (Key, IV) = new DNVEncryption().GenerateAESKeyFromPassword(Pass, salt, securityLevel);
                                                    if (new DNVEncryption().VerifyControlBlock(controlBlock, Key, IV, salt))
                                                    {
                                                        // Hasło pasuje do bloku kontrolnego i zostaje odszyfrowane
                                                        mainData = new DNVEncryption().DecryptData(mainData, Key, IV, salt); // Odszyfrowywanie
                                                    }
                                                }
                                            }
                                            else throw new Exception("Password is required");
                                        }

                                        // Dekompresuj zawartość
                                        if (_metadata["compression"].Value("enable").Get(false))
                                        {
                                            mainData = new ZstdHelper().Decompress(mainData);
                                        }

                                        // Inicjalizujemy obiekty `Container` dla mainData
                                        if (mainData == null || mainData.Length <= 4)
                                        {
                                            _conMain = new Container("Main", null);
                                        }
                                        else
                                        {
                                            int mainEndIndex = BitConverter.ToInt32(mainData, 0);
                                            if (mainEndIndex <= 4 || mainData.Length < mainEndIndex)
                                            {
                                                _conMain = new Container("Main", null);
                                            }
                                            else
                                            {
                                                _conMain = new Container(mainData, null, new advInt32().Set(4), mainEndIndex);
                                            }
                                        }
                                        _conMain.ValueUpdated += ConMain_ValueUpdated;
                                        _metadata.Value("openCounter").Add(1);
                                    }
                                }
                                else
                                    throw new Exception("DNV File is not supported. Using version is too old");
                            }
                            else
                            {
                                var localConMain = new Container("Main", null);
                                var localMetadata = new Container("MetaData", null);

                                try
                                {
                                    // Ładowanie zawartości do Main
                                    OlderDNV.ReadOlderDNV(reader.ReadBytes((int)(fs.Length - fs.Position)), localConMain, stringBytes);

                                    if (localConMain != null)
                                    {
                                        _conMain = localConMain;
                                        _metadata = localMetadata;
                                    }
                                }
                                catch
                                {
                                    // Cannot parse older DNV format — fall back to empty containers instead of failing
                                    _conMain = localConMain;
                                    _metadata = localMetadata;
                                    // Do not throw to allow opening an unknown/old file format for inspection
                                }
                            }
                        }
                    }
                    else
                    {
                        _conMain = new Container("Main", null);
                        _conMain.ValueUpdated += ConMain_ValueUpdated;
                        _metadata = new Container("MetaData", null);
                    }

                    isOpened = true;
                }
            }

            return isOpened;
        }

        private void ConMain_ValueUpdated()
        {
            autoSaveTimer?.Start();
        }

        /// <summary>
        /// Save the current state to the file
        /// </summary>
        public void Save()
        {
            lock (_savingLock)
            {
                autoSaveTimer?.Stop();
                if (_conMain == null || _metadata == null || !isOpened)
                    return;

                // Zapisywanie metadanych
                _metadata.Value("saveCounter").Add(1);
                _metadata["compression"].Value("enable").Set(false);

                byte[]? salt = null;
                byte[]? Key = null, IV = null;

                if (Pass != string.Empty)
                {
                    var securityLevel = DNVEncryption.SecurityLevel.Optimal;
                    salt = new DNVEncryption().RandomSalt();
                    _metadata["encryption"].Value("salt").Set(salt);
                    _metadata["encryption"].Value("securityLevel").Set((int)securityLevel);
                    (Key, IV) = new DNVEncryption().GenerateAESKeyFromPassword(Pass, salt, securityLevel);
                    _metadata["encryption"].Value("controlBlock").Set(new DNVEncryption().GenerateControlBlock(Key, IV, salt));
                }
                else
                    _metadata["encryption"].DropContainer();

                string fullUser = Environment.UserDomainName + "\\" + Environment.UserName;

                if (!_metadata["user"].Value("author").IsSet())
                {
                    _metadata["user"].Value("author").Set(fullUser);
                    _metadata["user"].Value("created").Set(DateTime.UtcNow);
                }

                _metadata["user"].Value("editor").Set(fullUser);
                _metadata["user"].Value("edited").Set(DateTime.UtcNow);

                // Przygotowanie do zapisu do pliku tymczasowego
                string tempFilePath = _FilePath + "tmp";
                long expectedSize = -1;

                using (FileStream fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write)) 
                {
                    using (BinaryWriter writer = new BinaryWriter(fs))
                    {
                        // Zapisujemy 16-znakowy string
                        byte[] stringBytes = Encoding.ASCII.GetBytes("DNVFile_byNylium");
                        writer.Write(stringBytes);

                        // Zapisujemy wersję (2 bajty)
                        writer.Write((ushort)_defaultProperties.MyVersion);

                        byte[] byteMain = _conMain.CompileContainer().ToArray();

                        _metadata["compression"].Value("initialSize").Set(byteMain.Length);

                        // Dokonaj kompresji
                        if (_defaultProperties.MinimumCompressionByteCount > 0 && byteMain.Length > _defaultProperties.MinimumCompressionByteCount)
                        {
                            _metadata["compression"].Value("enable").Set(true);

                            byte compressionLevel = _defaultProperties.CompressionLevel;
                            _metadata["compression"].Value("compressionLevel").Set(compressionLevel);

                            byteMain = new ZstdHelper().Compress(byteMain, compressionLevel);
                        }
                        else
                        {
                            _metadata["compression"].Value("enable").Set(false);
                            _metadata["compression"].Value("compressionLevel").Drop();
                        }

                        // Jeżeli uruchomiono opcje szyfrowania to ZASZYFRUJ Main
                        if (salt != null)
                            byteMain = new DNVEncryption().EncryptData(byteMain, Key, IV, salt);

                        // Ostateczna wielkość danych Main
                        _metadata["compression"].Value("finalSize").Set(byteMain.Length);

                        // Zabezpiecz Metadane
                        byte[] byteMetadata = _metadata.CompileContainer().ToArray();
                        new DNVEncryption().SimpleEncryptInPlace(byteMetadata, _defaultProperties.MetadataEncryption);

                        // Sprawdzamy, czy dane nie przekraczają maksymalnego rozmiaru
                        if (byteMain.Length + byteMetadata.Length + 4096 >= int.MaxValue)
                            throw new Exception("DNV is too long");

                        // Zapisujemy rozmiar metadanych (4 bajty)
                        writer.Write(BitConverter.GetBytes(byteMetadata.Length));

                        // Zapisujemy dane `metadata` i `main` bezpośrednio do pliku
                        writer.Write(byteMetadata);
                        writer.Write(byteMain);

                        writer.Flush();
                        fs.Flush(true);

                        expectedSize = fs.Position;
                    }
                }

                // Sprawdzamy, czy plik tymczasowy został poprawnie zapisany
                long actualSize = new FileInfo(tempFilePath).Length;
                if (actualSize != expectedSize)
                    throw new Exception($"Zapisany plik {tempFilePath} ma nieoczekiwany rozmiar ({actualSize}B z {expectedSize}B) – zapis uszkodzony, akcja przerwana.");

                // Przenosimy plik tymczasowy do docelowej lokalizacji
                if (File.Exists(_FilePath))
                    File.Replace(tempFilePath, _FilePath, null);
                else
                    File.Move(tempFilePath, _FilePath);
            }
        }

        public void SaveAndClose()
        {
            Save();
            Close();
        }

        public void Close()
        {
            lock (_savingLock)
            {
                if (isOpened)
                {
                    if (autoSaveTimer != null)
                    {
                        autoSaveTimer.Stop();
                        autoSaveTimer.Dispose();
                        autoSaveTimer = null;
                    }

                    _conMain = null;
                    _metadata = null;

                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    isOpened = false;
                }
            }
        }

        public Container main { 
            get {
                if (isOpened && _conMain != null)
                    return _conMain;
                else
                    throw new Exception("First open the DNV file");
            } 
        }

        public class iProperties 
        {
            private DNV DNVParent;
            internal iProperties(DNV DNVParent) { this.DNVParent = DNVParent; }
        }

        public class iMeta
        {
            private DNV DNVParent;
            internal iMeta(DNV DNVParent) { this.DNVParent = DNVParent; }

            private T GetMetadataValue<T>(string key, string? subkey = null, T? defaultValue = default)
            {
                if (DNVParent.isOpened && DNVParent._metadata != null)
                {
                    var section = string.IsNullOrEmpty(subkey) ? DNVParent._metadata : DNVParent._metadata[key];
                    if (defaultValue == null)
                        return section.Value(subkey ?? key).Get<T>();
                    else
                        return section.Value(subkey ?? key).Get(defaultValue);
                }
                else
                {
                    throw new Exception("First open the DNV file");
                }

                // Uproszczona wersja poniższej funkcji:
                //  if (DNVParent.isOpened && DNVParent.metadata != null)
                //      return DNVParent.metadata["encryption"].Value("enable").Get(false);
                //  else
                //      throw new Exception("First open the DNV file");
            }

            public bool isCompressed => GetMetadataValue("compression", "enable", false);
            public long initialDataSize => GetMetadataValue("compression", "initialSize", GetMetadataValue("compression", "finalSize", 0L));
            public long compressedDataSize => GetMetadataValue("compression", "finalSize", 0L);

            public long numberOfOpening => GetMetadataValue("openCounter", defaultValue: 0L);
            public long numberOfSaving => GetMetadataValue("saveCounter", defaultValue: 0L);

            public string author => GetMetadataValue("user", "author", defaultValue: "N/A");
            public string editor => GetMetadataValue("user", "editor", defaultValue: "N/A");
            public DateTime created => GetMetadataValue("user", "created", defaultValue: DateTime.MinValue);
            public DateTime edited => GetMetadataValue("user", "edited", defaultValue: DateTime.MinValue);

            public bool isEncrypted => GetMetadataValue("encryption", "enable", false);
            public int encryptionLevel => GetMetadataValue("encryption", "securityLevel", 0);
        }
    }
}