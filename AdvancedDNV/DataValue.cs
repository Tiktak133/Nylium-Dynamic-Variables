using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedDNV
{
    /// <summary>
    /// An object holding values ​​for the DNV library
    /// </summary>
    public class Value
    {
        public string ValueName { get; private set; }
        /// <summary>
        /// Object type, value type ​​of this Value
        /// </summary>
        public Type Type { get { return type; } }
        private Container _parent;

        internal Type type = null; //Typ wartości
        internal byte[] value;

        DNVProperties _myProperties = new();
        private object _engagedElementLock; // Blokuje dostęp do Value, gdy jest on używany przez inny wątek

        internal Value(string name, Container par)
        {
            var nameb = Encoding.UTF8.GetByteCount(name);
            if (nameb >= 32768)
                throw new ArgumentException("Value name is too long");

            ValueName = name;
            _parent = par;
            _engagedElementLock = new object();
        }

        /// <summary>
        /// Check if the value is assigned
        /// </summary>
        public bool IsSet()
        {
            lock (_engagedElementLock)
            {
                if (_parent == null) // Jeżeli Value zostanie Droped do momentu wywołania tej metody
                    return false;

                return (value != null && type != null);
            }
        }

        /// <summary>
        /// Adding/Summing a value to an already stored value in an object, doesn't work with all types
        /// </summary>
        /// <param name="value"></param>
        public Value Add(dynamic value)
        {
            if (value == null)
            {
                Set(value);
                return this;
            }

            lock (_engagedElementLock)
            {
                if (_parent != null) // Jeżeli Value zostanie Droped do momentu wywołania tej metody
                {
                    try
                    {
                        if (_myProperties.ListOfTypes.ContainsKey(value.GetType()))
                        {
                            var oldValue = Get(); //Pobieranie aktualnie zapisanej wartości

                            if (oldValue is bool)
                            {
                                this.value = BitConverter.GetBytes(value);
                                type = typeof(bool);
                            }
                            else if (oldValue is int || oldValue is uint || oldValue is short || oldValue is ushort || oldValue is long || oldValue is ulong || oldValue is byte)
                            {
                                oldValue = oldValue + value;

                                dynamic newValue = 0;
                                if (oldValue < 0)
                                {
                                    if (oldValue < short.MinValue)
                                    {
                                        if (oldValue < int.MinValue)
                                            newValue = (long)oldValue;
                                        else
                                            newValue = (int)oldValue;
                                    }
                                    else
                                        newValue = (short)oldValue;
                                    this.value = BitConverter.GetBytes(newValue);
                                    type = newValue.GetType();
                                }
                                else
                                {
                                    if (oldValue > byte.MaxValue)
                                    {
                                        if (oldValue > ushort.MaxValue)
                                        {
                                            if (oldValue > uint.MaxValue)
                                                newValue = (ulong)oldValue;
                                            else
                                                newValue = (uint)oldValue;
                                        }
                                        else
                                            newValue = (ushort)oldValue;
                                        this.value = BitConverter.GetBytes(newValue);
                                        type = newValue.GetType();
                                    }
                                    else
                                    {
                                        newValue = (byte)oldValue;
                                        this.value = new byte[] { newValue };
                                        type = newValue.GetType();
                                    }
                                }
                            }
                            else if (oldValue is null && (value is int || value is uint || value is short || value is ushort || value is long || value is ulong || value is byte))
                            {
                                oldValue = value; //Domyślnie 0 + value (nowa wartość)

                                dynamic newValue = 0;
                                if (oldValue < 0)
                                {
                                    if (oldValue < short.MinValue)
                                    {
                                        if (oldValue < int.MinValue)
                                            newValue = (long)oldValue;
                                        else
                                            newValue = (int)oldValue;
                                    }
                                    else
                                        newValue = (short)oldValue;
                                    this.value = BitConverter.GetBytes(newValue);
                                    type = newValue.GetType();
                                }
                                else
                                {
                                    if (oldValue > byte.MaxValue)
                                    {
                                        if (oldValue > ushort.MaxValue)
                                        {
                                            if (oldValue > uint.MaxValue)
                                                newValue = (ulong)oldValue;
                                            else
                                                newValue = (uint)oldValue;
                                        }
                                        else
                                            newValue = (ushort)oldValue;
                                        this.value = BitConverter.GetBytes(newValue);
                                        type = newValue.GetType();
                                    }
                                    else
                                    {
                                        newValue = (byte)oldValue;
                                        this.value = new byte[] { newValue };
                                        type = newValue.GetType();
                                    }
                                }
                            }
                            else
                            {
                                try
                                {
                                    string tryS = oldValue + value.ToString();
                                    this.value = Encoding.UTF8.GetBytes(tryS);
                                    type = typeof(string);
                                }
                                catch
                                {
                                    type = null;
                                    _parent.ErrorLog("Type of this variable is not supported");
                                    return this;
                                }
                            }

                            _parent.OnValueUpdated();
                        }
                        else
                        {
                            _parent.ErrorLog("Type of this variable is not supported");
                        }
                    }
                    catch
                    {
                        _parent.ErrorLog("Type of this variable is not supported");
                        //Nie poprawny typ
                    }
                }
            }
            return this;
        }

        /// <summary>
        /// Setting a value to an object
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public Value Set(dynamic value)
        {
            lock (_engagedElementLock) // Blokuje dostęp do Value, gdy jest on używany przez inny wątek
            {
                if (_parent != null) // Jeżeli Value zostanie Droped do momentu wywołania tej metody
                {
                    try
                    {
                        if (_myProperties.ListOfTypes.ContainsKey(value.GetType()))
                        {
                            type = value.GetType();
                            if (value is bool)
                            {
                                this.value = BitConverter.GetBytes(value);
                                type = typeof(bool);
                            }
                            else if (value is int || value is uint || value is short || value is ushort || value is long || value is ulong)
                            {
                                dynamic newValue = 0;
                                if (value < 0)
                                {
                                    if (value < short.MinValue)
                                    {
                                        if (value < int.MinValue)
                                            newValue = (long)value;
                                        else
                                            newValue = (int)value;
                                    }
                                    else
                                        newValue = (short)value;
                                    this.value = BitConverter.GetBytes(newValue);
                                }
                                else
                                {
                                    if (value > byte.MaxValue)
                                    {
                                        if (value > ushort.MaxValue)
                                        {
                                            if (value > uint.MaxValue)
                                                newValue = (ulong)value;
                                            else
                                                newValue = (uint)value;
                                        }
                                        else
                                            newValue = (ushort)value;
                                        this.value = BitConverter.GetBytes(newValue);
                                    }
                                    else
                                    {
                                        newValue = (byte)value;
                                        this.value = new byte[] { newValue };
                                    }

                                }
                                type = newValue.GetType();
                            }
                            else if (value is float || value is double || value is decimal)
                            {
                                // Wybór najwłaściwszego typu zmiennoprzecinkowego
                                if (value is decimal decimalValue)
                                {
                                    // Sprawdzenie, czy decimalValue może być reprezentowane jako float lub double
                                    if ((decimal)(float)decimalValue == decimalValue)
                                    {
                                        // Reprezentacja jako float
                                        this.value = BitConverter.GetBytes((float)decimalValue);
                                        type = typeof(float);
                                    }
                                    else if ((decimal)(double)decimalValue == decimalValue)
                                    {
                                        // Reprezentacja jako double
                                        this.value = BitConverter.GetBytes((double)decimalValue);
                                        type = typeof(double);
                                    }
                                    else
                                    {
                                        // Reprezentacja jako decimal
                                        int[] bits = decimal.GetBits(decimalValue);
                                        this.value = new byte[16];
                                        Buffer.BlockCopy(bits, 0, this.value, 0, 16);
                                        type = typeof(decimal);
                                    }
                                }
                                else if (value is double doubleValue)
                                {
                                    // Sprawdzenie, czy doubleValue może być reprezentowane jako float
                                    if ((double)(float)doubleValue == doubleValue)
                                    {
                                        // Reprezentacja jako float
                                        this.value = BitConverter.GetBytes((float)doubleValue);
                                        type = typeof(float);
                                    }
                                    else
                                    {
                                        // Reprezentacja jako double
                                        this.value = BitConverter.GetBytes(doubleValue);
                                        type = typeof(double);
                                    }
                                }
                                else if (value is float floatValue)
                                {
                                    // Bezpośrednia reprezentacja jako float
                                    this.value = BitConverter.GetBytes(floatValue);
                                    type = typeof(float);
                                }
                            }
                            else if (value is DateTime)
                            {
                                long dateTimeTicks = ((DateTime)value).Ticks;
                                this.value = BitConverter.GetBytes(dateTimeTicks);
                                type = typeof(DateTime);
                            }
                            else if (value is TimeSpan)
                            {
                                long timeSpanTicks = ((TimeSpan)value).Ticks;
                                this.value = BitConverter.GetBytes(timeSpanTicks);
                                type = typeof(TimeSpan);
                            }
                            else if (value is Guid)
                            {
                                if ((Guid)value == Guid.Empty)
                                    throw new Exception();

                                this.value = ((Guid)value).ToByteArray();
                                type = typeof(Guid);

                            }
                            else if (value is byte)
                            {
                                this.value = new byte[] { value };
                                type = typeof(byte);
                            }
                            else if (value is byte[] || value is List<byte>)
                            {
                                this.value = value;
                                type = typeof(byte[]);
                            }
                            else if (value is int[])
                            {
                                int[] intArray = (int[])value;
                                byte[] bytesIntArray = new byte[intArray.Length * sizeof(int)];
                                Buffer.BlockCopy(intArray, 0, bytesIntArray, 0, bytesIntArray.Length);

                                this.value = bytesIntArray;
                                type = typeof(int[]);
                            }
                            else if (value is long[])
                            {
                                long[] longArray = (long[])value;
                                byte[] bytesLongArray = new byte[longArray.Length * sizeof(long)];
                                Buffer.BlockCopy(longArray, 0, bytesLongArray, 0, bytesLongArray.Length);

                                this.value = bytesLongArray;
                                type = typeof(long[]);
                            }
                            else if (value is double[])
                            {
                                double[] doubleArray = (double[])value;
                                byte[] bytesDoubleArray = new byte[doubleArray.Length * sizeof(double)];
                                Buffer.BlockCopy(doubleArray, 0, bytesDoubleArray, 0, bytesDoubleArray.Length);

                                this.value = bytesDoubleArray;
                                type = typeof(double[]);
                            }
                            else if (value is string[])
                            {
                                string[] stringArray = (string[])value;
                                List<byte> byteList = new List<byte>();

                                foreach (var str in stringArray)
                                {
                                    byte[] stringBytes = Encoding.UTF8.GetBytes(str); // Zakodowanie string do byte[]
                                    int length = stringBytes.Length;

                                    byteList.AddRange(BitConverter.GetBytes(length)); // Zapisanie długości stringu jako int (4 bajty)
                                    byteList.AddRange(stringBytes); // Zapisanie bajtów stringu
                                }

                                this.value = byteList.ToArray();
                                this.type = typeof(string[]);
                            }
                            else
                            {
                                try
                                {
                                    string tryS = value.ToString();
                                    this.value = Encoding.UTF8.GetBytes(tryS);
                                    type = value.GetType();
                                }
                                catch
                                {
                                    type = null;
                                    _parent.ErrorLog("Type of this variable is not supported");
                                    return this;
                                }
                            }

                            _parent.OnValueUpdated();
                        }
                        else
                        {
                            _parent.ErrorLog("Type of this variable is not supported");
                        }
                    }
                    catch
                    {
                        _parent.ErrorLog("Type of this variable is not supported");
                        //Nie poprawny typ
                    }
                }
            }
            return this;
        }

        private dynamic transformToType(Type in_type)
        {
            if (in_type == typeof(bool))
                return BitConverter.ToBoolean(value, 0);
            else if (in_type == typeof(string))
                return Encoding.UTF8.GetString(value);
            else if (in_type == typeof(short))
                return BitConverter.ToInt16(value, 0);
            else if (in_type == typeof(ushort))
                return BitConverter.ToUInt16(value, 0);
            else if (in_type == typeof(int))
                return BitConverter.ToInt32(value, 0);
            else if (in_type == typeof(uint))
                return BitConverter.ToUInt32(value, 0);
            else if (in_type == typeof(long))
                return BitConverter.ToInt64(value, 0);
            else if (in_type == typeof(ulong))
                return BitConverter.ToUInt64(value, 0);
            else if (in_type == typeof(byte))
                return Convert.ToByte(value[0]);
            else if (in_type == typeof(float))
                return BitConverter.ToSingle(value, 0);
            else if (in_type == typeof(double))
                return BitConverter.ToDouble(value, 0);
            else if (in_type == typeof(decimal))
            {
                int[] bits = new int[4];
                Buffer.BlockCopy(value, 0, bits, 0, 16);
                return new decimal(bits);
            }
            else if (in_type == typeof(DateTime))
                return new DateTime(BitConverter.ToInt64(value, 0));
            else if (in_type == typeof(TimeSpan))
                return new TimeSpan(BitConverter.ToInt64(value, 0));
            else if (in_type == typeof(Guid))
                return new Guid(value);
            else if (in_type == null)
                return null;
            else if (in_type == typeof(byte[]))
            {
                byte[] result = new byte[value.Length];
                Array.Copy(value, result, value.Length);

                return result;
            }
            else if (in_type == typeof(int[]))
            {
                int[] liczbyIntOdwrotnie = new int[value.Length / sizeof(int)];
                Buffer.BlockCopy(value, 0, liczbyIntOdwrotnie, 0, value.Length);

                return liczbyIntOdwrotnie;
            }
            else if (in_type == typeof(long[]))
            {
                long[] liczbyLongOdwrotnie = new long[value.Length / sizeof(long)];
                Buffer.BlockCopy(value, 0, liczbyLongOdwrotnie, 0, value.Length);

                return liczbyLongOdwrotnie;
            }
            else if (in_type == typeof(double[]))
            {
                double[] liczbyDoubleOdwrotnie = new double[value.Length / sizeof(double)];
                Buffer.BlockCopy(value, 0, liczbyDoubleOdwrotnie, 0, value.Length);

                return liczbyDoubleOdwrotnie;
            }
            else if (in_type == typeof(string[])) 
            {
                List<string> stringList = new List<string>();
                int index = 0;

                while (index < this.value.Length)
                {
                    int length = BitConverter.ToInt32(this.value, index); // Odczytujemy długość stringu
                    index += sizeof(int);

                    byte[] stringBytes = new byte[length];
                    Array.Copy(this.value, index, stringBytes, 0, length); // Kopiujemy bajty stringu
                    index += length;

                    stringList.Add(Encoding.UTF8.GetString(stringBytes)); // Konwertujemy bajty na string
                }

                return stringList.ToArray();
            }

            throw new Exception("Type is not supported");
        }

        /// <summary>
        /// Returns the value ​​of given type
        /// </summary>
        /// <typeparam name="T">The value ​​will take this type</typeparam>
        /// <returns>The value of the Value object</returns>
        public T Get<T>()
        {
            lock (_engagedElementLock)
            {
                if (value != null && value.Length > 0)
                {
                    return (T)transformToType(type);
                }
                return (T)(object)null;
            }
        }

        /// <summary>
        /// Returns the default value or, if one exists, a stored value with the default value type
        /// </summary>
        /// <param name="defaultOut">Default value, used if no other one is saved</param>
        /// <returns>The value of the Value object</returns>
        public dynamic Get(dynamic defaultOut)
        {
            lock (_engagedElementLock)
            {
                if (value != null && value.Length > 0)
                {
                    dynamic getValue = transformToType(type);
                    if (getValue != null)
                    {
                        return getValue;
                    }
                }
                return defaultOut;
            }
        }

        /// <summary>
        /// Returns the value obtained automatically
        /// </summary>
        /// <returns>>The value of the Value object</returns>
        public dynamic Get()
        {
            lock (_engagedElementLock)
            {
                if (value != null && value.Length > 0)
                {
                    return transformToType(type);
                }
                return null;
            }
        }

        public void Drop()
        {
            Container parent;

            lock (_engagedElementLock)
            {
                // zabezpieczenie przed podwójnym Drop()
                if (_parent == null)
                    return;

                parent = _parent;

                value = null;
                type = null;

                _parent = null; // zerwij wcześnie
            }

            // USUWANIE POZA LOCKIEM (ważne)
            parent.ValuesList.Remove(ValueName);
            parent.GlobalValuesSet.Remove(this);
            parent.OnValueUpdated();
        }

        protected internal void GetBytes(List<byte> indexes, List<byte> data)
        {
            lock ( _engagedElementLock )
            {
                if (value == null || value.Length == 0 || Type == null || !_myProperties.ListOfTypes.TryGetValue(Type, out var typeByte))
                    return;

                string name = this.ValueName;
                byte[] nameBytes = Encoding.UTF8.GetBytes(ValueName);
                int nameLength = nameBytes.Length;
                if (nameLength >= 32768)
                    throw new ArgumentException("Value name is too long");

                // INDEX
                List<byte> newIndexes = new List<byte>();
                newIndexes.AddRange(BitConverter.GetBytes((ushort)(nameLength + 32768))); //Dodawanie długości wartości nazwy (+1 bit na samym przodzie określający, że jest to Value, a nie Container)
                newIndexes.AddRange(nameBytes); //Dodawanie nazwy wartości w UTF8 
                newIndexes.Add(typeByte); //Typ zmiennej

                //DATA
                var newData = new List<byte>();
                newData.Add(0); //Dodatkowe dane do zmiennej [aktualnie nie wykorzystane 0 - brak danych]
                newData.AddRange(value);

                //INDEX FINISHING
                newIndexes.AddRange(BitConverter.GetBytes((uint)data.Count)); //Ostatni index z sekcji Data do którego będą przypisane dane z tej WARTOŚCI (VALUE) - miejsce zapisu danych dla tego obiektu VALUE
                newIndexes.AddRange(BitConverter.GetBytes((uint)newData.Count)); //Ilość bajtów która zostaje zarezerwowana w sekcji Data

                //COMPILE
                indexes.AddRange(newIndexes);
                data.AddRange(newData);
            }        
        }
    }
}
