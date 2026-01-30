# SwEng26_Group3

## Overview

The solution is split into 4 projects:

- DropMe
    - This project contains all of the cross platform code and service interfaces.
    - Code should go in here whenever possible, including cross platform implementations of services that can be eclipsed by platform specific versions.
- DropMe.Desktop
    - Contains all of the desktop (Windows, MacOS, Linux) specific code.
    - This should be able to build for any of these platforms.
- DropMe.Android
    - Contains all of the Android specific code.
    - Can utilise Android specific Java libraries through c# bindings.
- DropMe.iOS
    - TODO.

The docker image built by the Dockerfile should be able to build for any of these platforms except for iOS.

## Building

- Go to the folder containing the project you want to build (e.g. `cd DropMe.Desktop`)
- Run `dotnet workload restore` followed by `dotnet build`

In this case the project will be built to `DropMe.Desktop/Debug/net10.0`. Run it with `dotnet DropMe.Desktop.dll`.

### Quick run

You can quickly build and run a project by running `dotnet run`.

### Android

To install the app to an android device over adb:

- Connect to the device over adb
- Run `adb install com.CompanyName.DropMe-Signed.apk`
- Accept any prompts on the device

This will then be installed like a regular app.

## Testing

Currently unit testing is only implemented for the DropMe project.
It is using the nunit library and can be tested from the DropMe folder by running `dotnet test`.

## Installation

Automatic installation is currently only supported for Archlinux.
Copy the PKGBUILD into an empty folder and run `makepkg -si`.