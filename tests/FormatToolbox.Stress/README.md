# 性能与稳定性测试

默认连续执行 3 轮、每轮 60 个三页 PDF 转换，并记录耗时、峰值工作集、每轮回收后的工作集、失败数及残留临时文件：

```powershell
dotnet run --project tests\FormatToolbox.Stress -c Release
```

可使用 `--items 200 --pages 10 --rounds 5` 扩大压力；`--keep` 保留生成样本。结果写入 `artifacts\performance\latest.json`。
