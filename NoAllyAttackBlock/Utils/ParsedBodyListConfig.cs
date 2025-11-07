using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NoAllyAttackBlock.Utils
{
    public sealed class ParsedBodyListConfig : IReadOnlyList<BodyIndex>, IDisposable
    {
        public ConfigEntry<string> Config { get; }

        IComparer<BodyIndex> _comparer = BodyIndexComparer.Instance;
        public IComparer<BodyIndex> Comparer
        {
            get
            {
                return _comparer;
            }
            set
            {
                _comparer = value;
                refreshValue();
            }
        }

        public int Count => _parsedArray.Length;

        public BodyIndex this[int index] => _parsedArray[index];

        BodyIndex[] _parsedArray = [];

        bool _catalogAvailable;
        bool _isDisposed;

        public event Action OnValueChanged;

        public ParsedBodyListConfig(ConfigEntry<string> config)
        {
            Config = config;
            Config.SettingChanged += Config_SettingChanged;

            BodyCatalog.availability.CallWhenAvailable(onCatalogAvailable);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            Config.SettingChanged -= Config_SettingChanged;
        }

        public IEnumerator<BodyIndex> GetEnumerator()
        {
            return ((IEnumerable<BodyIndex>)_parsedArray).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _parsedArray.GetEnumerator();
        }

        public int IndexOf(BodyIndex value)
        {
            if (_parsedArray == null || _parsedArray.Length == 0)
                return -1;

            int index = Array.BinarySearch(_parsedArray, value, Comparer);
            return index >= 0 ? index : -1;
        }

        public bool Contains(BodyIndex value)
        {
            return IndexOf(value) >= 0;
        }

        void Config_SettingChanged(object sender, EventArgs e)
        {
            refreshValue();
        }

        void onCatalogAvailable()
        {
            _catalogAvailable = true;
            refreshValue();
        }

        void refreshValue()
        {
            if (!_catalogAvailable)
                return;

            string[] splitInput = Config.Value.Split([","], StringSplitOptions.RemoveEmptyEntries);

            List<BodyIndex> parsedBodyIndices = new List<BodyIndex>(splitInput.Length);

            for (int i = 0; i < splitInput.Length; i++)
            {
                string bodyName = splitInput[i].Trim();

                if (parseBodyIndex(bodyName, out BodyIndex bodyIndex))
                {
                    parsedBodyIndices.Add(bodyIndex);
                }
                else
                {
                    Log.Warning($"Failed to find character matching '{bodyName}' for '{Config.Definition}'");
                }
            }

            _parsedArray = [.. parsedBodyIndices];
            Array.Sort(_parsedArray, Comparer);

            Log.Debug($"Parsed body list value ({Config.Definition}): [{string.Join(", ", _parsedArray.Select(BodyCatalog.GetBodyName))}]");

            OnValueChanged?.Invoke();
        }

        bool parseBodyIndex(string input, out BodyIndex bodyIndex)
        {
            bodyIndex = BodyCatalog.FindBodyIndex(input);
            if (bodyIndex != BodyIndex.None)
                return false;

            static bool checkName(string name, string input)
            {
                StringBuilder filteredNameBuilder = HG.StringBuilderPool.RentStringBuilder();
                StringBuilder asciiOnlyNameBuilder = HG.StringBuilderPool.RentStringBuilder();
                StringBuilder alphaNumericOnlyNameBuilder = HG.StringBuilderPool.RentStringBuilder();

                try
                {
                    for (int i = 0; i < name.Length; i++)
                    {
                        if (char.IsWhiteSpace(name[i]) || name[i] == ',')
                            continue;

                        filteredNameBuilder.Append(name[i]);

                        if (name[i] <= 0x7F)
                        {
                            asciiOnlyNameBuilder.Append(name[i]);
                        }

                        if (char.IsLetterOrDigit(name[i]))
                        {
                            alphaNumericOnlyNameBuilder.Append(name[i]);
                        }
                    }

                    return (filteredNameBuilder.Length > 0 && string.Equals(filteredNameBuilder.ToString(), input, StringComparison.OrdinalIgnoreCase)) ||
                           (asciiOnlyNameBuilder.Length > 0 && string.Equals(asciiOnlyNameBuilder.ToString(), input, StringComparison.OrdinalIgnoreCase)) ||
                           (alphaNumericOnlyNameBuilder.Length > 0 && string.Equals(alphaNumericOnlyNameBuilder.ToString(), input, StringComparison.OrdinalIgnoreCase));
                }
                finally
                {
                    alphaNumericOnlyNameBuilder = HG.StringBuilderPool.ReturnStringBuilder(alphaNumericOnlyNameBuilder);
                    asciiOnlyNameBuilder = HG.StringBuilderPool.ReturnStringBuilder(asciiOnlyNameBuilder);
                    filteredNameBuilder = HG.StringBuilderPool.ReturnStringBuilder(filteredNameBuilder);
                }
            }

            foreach (CharacterBody body in BodyCatalog.allBodyPrefabBodyBodyComponents)
            {
                string bodyName = body.name;
                if ((!string.IsNullOrWhiteSpace(bodyName) &&
                    (checkName(bodyName, input) || (bodyName.EndsWith("body", StringComparison.OrdinalIgnoreCase) && bodyName.Length > 4 && checkName(bodyName[0..^4], input)))) ||
                    (!string.IsNullOrWhiteSpace(body.baseNameToken) && checkName(body.baseNameToken, input)) ||
                    (Language.english.TokenIsRegistered(body.baseNameToken) && checkName(Language.english.GetLocalizedStringByToken(body.baseNameToken), input)))
                {
                    bodyIndex = body.bodyIndex;
                    return true;
                }
            }

            bodyIndex = BodyIndex.None;
            return false;
        }
    }
}
