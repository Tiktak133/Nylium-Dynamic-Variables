# AdvancedDNV (DNVFrame)

> Binary, encrypted data storage & exchange framework  
> Used in **Nylium services**

---

## ✨ Overview

**AdvancedDNV** is a high-performance binary data framework designed for:

- fast variable persistence
- automatic saving (autosave)
- secure import / export
- network communication

It can be compared to **JSON**, but:

- data is stored **binary**
- structure is **hierarchical**
- files are **encrypted**
- files are **unreadable without the library**
- optimized for backend & services

---

## 🧱 Architecture

```
DNV
 ├── Main (Container)
 │    ├── Container
 │    │    └── Value
 │    └── Value
 └── MetaData (Container)
```

### Core concepts

| Element | Description |
|------|-------------|
| `DNV` | File handler, autosave, encryption |
| `DNVFrame` | In-memory / network data frame |
| `Container` | Hierarchical structure (like folders) |
| `Value` | Single typed variable |

---

## 📦 Supported Data Types

### Primitive
- `bool`
- `string`

### Integer numbers
- `byte`, `short`, `int`, `long`
- `ushort`, `uint`, `ulong`

### Floating-point
- `float`, `double`, `decimal`

### Date & special
- `DateTime`
- `TimeSpan`
- `Guid`

### Collections
- `byte[]`
- `int[]`
- `long[]`
- `double[]`
- `string[]`

---

## 📁 Container

Containers behave like folders.

- can contain other containers
- can contain values
- names are validated
- changes trigger autosave

```csharp
var user = main["User"];
```

---

## 🧩 Value

Represents a single stored variable.

- strongly typed
- stored internally as `byte[]`
- thread-safe
- change-tracked

```csharp
// Setting data to value (two ways)
user.SetValue("Name", "Tiktak133");
user.Value("Age").Set(25);

// Extracting data
var age = user.Value("Age").Get();
```

---

## 🧠 DNV

Main file handler.

### Features
- encrypted binary file
- autosave with timer
- thread-safe save lock
- metadata support

### Constructors
```csharp
DNV(string filePath) //By default, AutoSave is enabled
DNV(string filePath, string password, bool autoSave)
DNV(string filePath, string password, int autoSaveDelayMs)
```

---

## 🧬 DNVFrame

Lightweight data representation.

Used for:
- network transfer
- export / import
- working without file access

### Constructors
```csharp
DNVFrame()
DNVFrame(byte[] recoveryData)
DNVFrame(string recoveryDataString)
```

---

## 🔐 Encryption

- symmetric encryption
- in-place byte encryption
- separate layers for:
  - metadata
  - data frame
- no plaintext structure

Without the library, files are **not readable**.

---

## 🔁 Example – Create & Save

```csharp
var dnv = new DNV("data.dnv", "secret-password", AutoSave: true);
var main = dnv.main;

var settings = main["Settings"];
settings.Value("Volume").Set(80);
settings.Value("Volume").Add(8);
settings.Value("Fullscreen").Set(true);

// Possibility to manually force saving when closing the program
// dnv.SaveAndClose();
```

---

## 🌐 Example – Network Transfer

### Export
```csharp
DNVFrame frame = dnv.ExportFrame();
byte[] payload = frame.ToBytes();
```

### Import
```csharp
DNVFrame frame = new DNVFrame(payload);
int volume = frame.Main["Settings"].Value("Volume").Get<int>();

var volume = frame.Main["Settings"].Value("Volume").Get();
```

---

## 🚀 Use cases

- backend services
- encrypted configuration storage
- network state synchronization
- secure local persistence

---

## 📄 License

Internal / private usage
