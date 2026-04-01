# SwEng26_Group3

## Overview

The solution is split into 4 projects:

- **DropMe**
  - Contains all cross-platform code and service interfaces.
  - Code should go here whenever possible, including cross-platform implementations of services that may be overridden by platform-specific versions.

- **DropMe.Desktop**
  - Contains all desktop-specific code (Windows, macOS, Linux).
  - Should be able to build for any of these platforms.

- **DropMe.Android**
  - Contains all Android-specific code.
  - Can utilise Android-specific Java libraries through C# bindings.

- **DropMe.iOS**
  - TODO.

The Docker image built by the `Dockerfile` should be able to build for all platforms except iOS.

---

## Building

1. Navigate to the folder of the project you want to build:
   ```bash
   cd DropMe.Desktop
   ```

2. Restore workloads:
   ```bash
   dotnet workload restore
   ```

3. Build the project:
   ```bash
   dotnet build
   ```

The project will be built to:
```
DropMe.Desktop/bin/Debug/net10.0/
```

Run it with:
```bash
dotnet DropMe.Desktop.dll
```

---

### Quick Run

You can build and run a project in one step:

```bash
dotnet run
```

---

### Android

To install the app on an Android device using ADB:

1. Connect to the device via ADB  
2. Run:
   ```bash
   adb install com.CompanyName.DropMe-Signed.apk
   ```
3. Accept any prompts on the device  

The app will be installed like a regular application.

---

## Testing

Unit testing is currently only implemented for the **DropMe** project.

- Framework: NUnit  

Run tests from the `DropMe` folder:

```bash
dotnet test
```

---

## Installation

Automatic installation is currently only supported for Arch Linux.

1. Copy the `PKGBUILD` file into an empty folder  
2. Run:
   ```bash
   makepkg -si
   ```