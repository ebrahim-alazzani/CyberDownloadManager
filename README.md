# Cyber Download Manager (CDM)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![Architecture](https://img.shields.io/badge/Architecture-Clean%20Architecture-brightgreen.svg)](#architecture)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue.svg)](#system-requirements)

---

## 🇬🇧 English Overview

**Cyber Download Manager (CDM)** is a modern, high-performance download accelerator and network transfer orchestrator engineered with **C# and .NET 9**. Built using **Clean Architecture** and **Domain-Driven Design (DDD)** principles, CDM delivers multi-segmented dynamic thread acceleration, intelligent bandwidth allocation, pause/resume fault-tolerance, and real-time network telemetry.

### ⚡ Key Features
* **Multi-Thread Acceleration:** Splits HTTP/HTTPS network streams into parallel chunks to maximize TCP throughput.
* **Resilient State Persistence:** Full pause and resume capability with byte-range `Range: bytes=` support and transaction safety.
* **Clean Architecture Design:** Strict segregation between Domain, Application, Infrastructure, and Presentation layers.
* **Intelligent Bandwidth Throttling:** Granular speed limits and priority queue scheduling for concurrent downloads.
* **Real-Time Telemetry:** Instantaneous speed metrics, active connections monitoring, and transfer progress calculation.
* **Non-Blocking Asynchronous Engine:** High-performance async I/O (`async/await`) with low memory and CPU overhead.

### 🏗️ Architectural Topology
```
CyberDownloadManager/
├── src/
│   ├── CyberDownloadManager.Domain/         # Core Entities, Value Objects, Domain Events & Interfaces
│   ├── CyberDownloadManager.Application/    # Use Cases, CQRS Handlers, Transfer Orchestrator & Services
│   ├── CyberDownloadManager.Infrastructure/ # Network Sockets, Multi-Thread Engine, Storage & Logging
│   ├── CyberDownloadManager.Presentation/   # Desktop GUI, ViewModels (MVVM) & User Interaction
│   └── CyberDownloadManager.Shared/         # Utilities, Common Contracts & Cross-Cutting Constants
├── tests/
│   ├── CyberDownloadManager.UnitTests/      # Engine unit tests & protocol verification
│   └── CyberDownloadManager.IntegrationTests/# End-to-end multi-thread transfer tests
```

### 🚀 Getting Started
```bash
# 1. Clone the repository
git clone ssh://git@ssh.github.com:443/ebrahim-alazzani/CyberDownloadManager.git
cd CyberDownloadManager

# 2. Restore dependencies
dotnet restore

# 3. Build the solution
dotnet build --configuration Release

# 4. Run application
dotnet run --project src/CyberDownloadManager.Presentation
```

---

## 🇸🇦 نظرة عامة باللغة العربية

**Cyber Download Manager (CDM)** هو نظام ومدير تحميلات متطور عالي الأداء لتسريع وتنسيق نقل البيانات والملفات عبر الشبكة، مبني بلغة **C# وإطار العمل .NET 9**. تم تصميم النظام بالكامل بالاعتماد على معمارية **Clean Architecture** ومبادئ **التصميم الموجه بالمجال (DDD)** لتوفير تسريع متعدد المسارات، إدارة ذكية للباندويث، استئناف التحميل عند انقطاع الاتصال، ومتابعة حية للشبكة.

### ⚡ المميزات الرئيسية
* **تسريع ديناميكي متعدد المسارات (Multi-Thread Segmentation):** تقسيم روابط التحميل إلى أجزاء متعددة تعمل بالتوازي لاستغلال سرعة الاتصال بالكامل.
* **استئناف التحميل بأمان (Pause & Resume):** دعم كامل لبروتوكول `Range: bytes=` مع الحفاظ على سلامة الملف من التلف عند انقطاع الإنترنت.
* **تصميم معماري نظيف (Clean Architecture):** فصل تام للمسؤوليات بين طبقات النطاق (Domain)، التطبيق (Application)، البنية التحتية (Infrastructure)، والواجهة (Presentation).
* **جدولة وتحكم بالسرعة:** إمكانية تحديد السرعة القصوى لكل ملف وترتيب أولويات طابور التنزيلات (Queue Prioritization).
* **مؤشرات لحظية دقيقة:** قياس فوري للسرعة، الوقت المتبقي (ETA)، وحجم البيانات المكتملة بدقة البايت.
* **محرك غير حاجب وخفيف الموارد:** مبني بالكامل على النمط غير المتزامن (`async/await`) مع استهلاك منخفض للذاكرة والمعالج.

### 🏗️ هيكل الطبقات البرمجية
* **Domain:** الكيانات الأساسية (DownloadItem)، وحالات التحميل، وقواعد الأعمال.
* **Application:** حالات الاستخدام وخدمات تنسيق التنزيلات ومحرك تجزئة الحزم.
* **Infrastructure:** خدمات الاتصال بالشبكة (Sockets/HttpClient)، التخزين، وحفظ البيانات بقواعد البيانات.
* **Presentation:** واجهة المستخدم المكتبية بأسلوب MVVM، وإدارة النوافذ وقوائم التحميل.
* **Shared:** الثوابت المشتركة ونتائج العمليات (Result Objects).

### 🚀 التشغيل والبناء
```bash
# استعادة حزم المشروع
dotnet restore

# بناء المشروع بنمط Release
dotnet build --configuration Release

# تشغيل واجهة البرنامج
dotnet run --project src/CyberDownloadManager.Presentation
```

---

## 📜 الترخيص (License)
هذا المشروع مرخص بموجب رخصة **MIT License**.
