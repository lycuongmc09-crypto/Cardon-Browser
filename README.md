# Cardon Browser

> **Sharp. Swift. Yours.**

A lightweight, open-source web browser for Windows, built with **C# / WPF** and powered by **Microsoft WebView2**. Cardon Browser is designed to be simple, fast to start, and easy to customize — a browser shell you can actually read, modify, and make your own.

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square)
![Engine](https://img.shields.io/badge/engine-WebView2-2C8EBB?style=flat-square)
![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)

---

## Table of Contents

- [About](#about)
- [Features](#features)
- [Screenshots](#screenshots)
- [Requirements](#requirements)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Customization](#customization)
- [Project Structure](#project-structure)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)

---

## About

Cardon Browser is a personal, independent rebuild of the original KMY Browser, with its own identity and branding. It keeps the same lightweight philosophy but adopts a distinct visual theme — **carbon and amber** — and its own internal pages.

The goal is simple: provide a minimal, readable, and hackable browser foundation on top of WebView2, without the weight of a full Electron-style stack. Because WebView2 shares the Chromium runtime already present on modern Windows systems, Cardon Browser stays small on disk and starts fast.

---

## Features

- **Lightweight core** — built on WebView2, which reuses the Edge runtime already installed on Windows 10/11. No bundled Chromium.
- **Native Windows UI** — C# + WPF, targeting .NET 8.
- **Custom internal pages** — `cardon://dashboard` and `cardon://about`. Legacy `kmy://` URLs are still supported.
- **Dashboard with quick links** — a clean start page built around the idea of *"cut through the noise"*.
- **Configurable search engines** — Brave, Google, Bing, or DuckDuckGo.
- **Configurable homepage** — Dashboard, Blank, or a custom URL.
- **Persistent history** — stored as JSON.
- **Persistent settings** — stored as JSON.
- **Carbon + amber theme** — a distinct visual identity from the original KMY Browser.

---

## Screenshots

> Screenshots will be added soon.

---

## Requirements

- **OS:** Windows 10 or Windows 11
- **Runtime:** [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  - Pre-installed on most Windows 11 systems.
  - May require manual installation on Windows 10.
- **SDK (for building):** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

---

## Getting Started

### Run from source

```bash
dotnet run --project KMY-Browser-Custom
