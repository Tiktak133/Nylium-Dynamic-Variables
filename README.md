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

### ⚠️ Memory Model

All loaded data is **kept entirely in RAM** for the lifetime of the `DNV` / `DNVFrame` instance.  
As a result:

- data access is very fast
- no partial loading or streaming is used
- memory usage grows with data size

**Recommended usage:**  
- small to medium datasets  
- configuration data  
- runtime state  
- backend/service metadata  

Not recommended for very large datasets.

---

## 🧱 Architecture

```
DNV
 ├── Main (Container) - default and first container in the structure
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
// main is defaut Container in `DNV` / `DNVFrame`

Container user = main["User"];
Container subf = main["User"]["subfolder"];
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
user.Value("Name").Set("Tiktak133");

// Extracting data
string? nickName = user.Value("Name").Get(); 
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

### Constructors
```csharp
DNVFrame()
DNVFrame(byte[] exportedData)
DNVFrame(string exportedDataString)
```

---

## 🔐 Encryption (Only DNV File)

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
DNV dnv = new("data.dnv", "secret-password", AutoSave: true);
Container main = dnv.main;

Container settings = main["Settings"];
settings.Value("Volume").Set(80);        // [main/Settings < Volume = 80]
settings.Value("Volume").Add(-8);        // [main/Settings < Volume = 72]
settings.Value("Fullscreen").Set(true);  // [main/Settings < Fullscreen = true]

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

// Using imported data (two ways)
int? volume = frame.main["Settings"].Value("Volume").Get<int?>();
dynamic volume = frame.main["Settings"].Value("Volume").Get();
```

---

## 🚀 Use cases

- backend services
- encrypted configuration storage
- network state synchronization
- secure local persistence

---
