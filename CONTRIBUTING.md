# Contributing to TiviIPTVPlayer

Thank you for your interest in contributing to **TiviIPTVPlayer**! We welcome contributions from developers, designers, and IPTV enthusiasts of all experience levels.

## How Can You Help?

- 🐛 **Report Bugs**: Submit detailed bug reports with steps to reproduce and system specs.
- 💡 **Suggest Features**: Open an issue describing ideas to improve the player experience.
- 💻 **Submit Pull Requests**: Fix issues, implement features, or optimize performance.
- 📖 **Improve Documentation**: Enhance instructions, clarify setup steps, or improve guides.
- 🎨 **UI/UX Polish**: Propose design refinements that align with modern Fluent Design principles.

---

## Development Setup

### Prerequisites

- **Windows 10 version 1809+** or **Windows 11**
- **.NET 8.0 SDK** (or higher)
- **Visual Studio 2022** (17.8 or later) with:
  - *.NET Desktop Development* workload
  - *Windows App SDK C# Templates* extension

### Getting Started

1. **Fork & Clone** the repository:
   ```bash
   git clone https://github.com/uponatime2019/TiviIPTVPlayer.git
   cd TiviIPTVPlayer
   ```

2. **Open the Solution**:
   Double-click `TiviIPTVPlayer.sln` to open it in Visual Studio 2022.

3. **Build the Project**:
   ```bash
   dotnet build "TiviIPTVPlayer.csproj" -p:Platform=x64
   ```

4. **Run the Project**:
   ```bash
   dotnet run --project "TiviIPTVPlayer.csproj"
   ```

---

## Pull Request Guidelines

1. **Create a branch** for your feature or bug fix:
   ```bash
   git checkout -b feature/your-feature-name
   ```
2. **Follow existing coding conventions**:
   - Clean, readable C# 12 / .NET 8 code
   - Nullable reference types enabled (`<Nullable>enable</Nullable>`)
   - Comprehensive error handling and non-crashing fallbacks
   - Meaningful commit messages
3. **Test locally**:
   - Ensure the Debug build compiles with **0 errors and 0 warnings**:
     ```bash
     dotnet build "TiviIPTVPlayer.csproj" -p:Platform=x64
     ```
   - Ensure the standalone Release publish builds and runs cleanly:
     ```bash
     dotnet publish "TiviIPTVPlayer.csproj" -c Release -p:Platform=x64 -o publish_test
     ```
4. **Submit your PR** with a clear explanation of what was changed and why.

---

## Code of Conduct

Be kind, respectful, and constructive. We strive to make this a friendly, open, and inclusive community for everyone.
