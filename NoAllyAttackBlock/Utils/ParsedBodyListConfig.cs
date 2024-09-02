using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NoAllyAttackBlock.Utils
{
    public class ParsedBodyListConfig : IReadOnlyList<BodyIndex>, IDisposable
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

        BodyIndex[] _parsedArray;

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

            List<BodyIndex> parsedArray = new List<BodyIndex>(splitInput.Length);

            for (int i = 0; i < splitInput.Length; i++)
            {
                splitInput[i] = splitInput[i].Trim();

                if (parseBodyIndex(splitInput[i], out BodyIndex bodyIndex))
                {
                    parsedArray.Add(bodyIndex);
                }
                else
                {
                    Log.Warning($"Failed to find character matching '{splitInput[i]}' for '{Config.Definition}'");
                }
            }

            _parsedArray = parsedArray.ToArray();
            Array.Sort(_parsedArray, Comparer);

#if DEBUG
            Log.Debug($"Parsed body list value ({Config.Definition}): [{string.Join(", ", _parsedArray.Select(BodyCatalog.GetBodyName))}]");
#endif

            OnValueChanged?.Invoke();
        }

        bool parseBodyIndex(string input, out BodyIndex bodyIndex)
        {
            bodyIndex = BodyCatalog.FindBodyIndex(input);
            if (bodyIndex != BodyIndex.None)
                return false;

            bool checkName(string name)
            {
                StringBuilder filteredNameBuilder = HG.StringBuilderPool.RentStringBuilder();
                StringBuilder asciiOnlyNameBuilder = HG.StringBuilderPool.RentStringBuilder();
                StringBuilder alphaNumericOnlyNameBuilder = HG.StringBuilderPool.RentStringBuilder();

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

                string filteredName = filteredNameBuilder.ToString();
                string asciiOnlyName = asciiOnlyNameBuilder.ToString();
                string alphaNumericName = alphaNumericOnlyNameBuilder.ToString();

                HG.StringBuilderPool.ReturnStringBuilder(alphaNumericOnlyNameBuilder);
                HG.StringBuilderPool.ReturnStringBuilder(asciiOnlyNameBuilder);
                HG.StringBuilderPool.ReturnStringBuilder(filteredNameBuilder);

                return string.Equals(filteredName, input, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(asciiOnlyName, input, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(alphaNumericName, input, StringComparison.OrdinalIgnoreCase);
            }

            foreach (CharacterBody body in BodyCatalog.allBodyPrefabBodyBodyComponents)
            {
                string bodyName = body.name;
                if (checkName(bodyName) ||
                    bodyName.EndsWith("body", StringComparison.OrdinalIgnoreCase) && checkName(bodyName.Remove(bodyName.Length - 4)) ||
                    checkName(body.baseNameToken) ||
                    !Language.IsTokenInvalid(body.baseNameToken) && checkName(Language.english.GetLocalizedStringByToken(body.baseNameToken)))
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
