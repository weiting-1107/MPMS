# MPMS 多專案管理系統

[![Open in GitHub Codespaces](https://github.com/codespaces/badge.svg)](https://codespaces.new/YOUR_GITHUB_USERNAME/YOUR_REPOSITORY_NAME)

本專案已完整配置 GitHub Codespaces 開發環境，您可以點擊上方的徽章按鈕一鍵啟動線上開發環境與獨立的 SQL Server 資料庫。

## 🚀 快速開始 (GitHub Codespaces)

當您點擊「Open in GitHub Codespaces」進入線上開發環境後，系統會自動在背景啟動一個 .NET 9 開發容器以及 SQL Server 資料庫，並自動完成資料庫初始化（建立資料表與填入初始資料）。

請按照以下步驟啟動系統：

### 步驟 1：開啟終端機 (Terminal)
1. 進入 Codespaces 網頁後，點擊左上角的選單圖示（三條線）。
2. 選擇 **Terminal** -> **New Terminal**（或使用快捷鍵 `Ctrl + ~`）。

### 步驟 2：執行網頁系統
在終端機中，輸入以下指令並按 Enter：
```bash
cd MPMS
dotnet run
```

### 步驟 3：開啟網頁
當系統成功編譯並啟動後，VS Code 右下角會彈出提示「Your application running on port 5105 is available.」，點擊 **Open in Browser** 即可開啟系統！
* 或者您也可以點擊下方狀態列的 **Ports** 標籤，找到 port `5105` 並點選地球圖示開啟網頁。

---

## 🛠️ 本地開發環境啟動

如果您是在本地（Windows 電腦）下載本專案，請先確認已安裝 **.NET 9.0 SDK** 與 **SQL Server**，並於 `/MPMS/appsettings.json` 設定好您的資料庫連線字串，接著執行以下指令：

```powershell
cd MPMS
dotnet run
```
系統預設啟動於 `http://localhost:5105`。
