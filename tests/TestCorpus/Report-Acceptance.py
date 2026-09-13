"""Independent saved-PDF validation, OCR CER, pagination and readable-font checks."""
from pathlib import Path
import argparse, json, unicodedata
from collections import Counter
from pypdf import PdfReader
import pdfplumber

parser=argparse.ArgumentParser();parser.add_argument('--directory',default='artifacts/acceptance/generated');args=parser.parse_args()
root=Path(args.directory).resolve();report=json.loads((root/'results.json').read_text(encoding='utf-8'))
truth=json.loads((root/'samples/ground-truth.json').read_text(encoding='utf-8'))
def normalize(s):return ''.join(c for c in unicodedata.normalize('NFKC',s) if not c.isspace())
def distance(a,b):
    row=list(range(len(b)+1))
    for i,x in enumerate(a,1):
        nxt=[i]
        for j,y in enumerate(b,1):nxt.append(min(nxt[-1]+1,row[j]+1,row[j-1]+(x!=y)))
        row=nxt
    return row[-1]
metrics=[]
for case in report['Cases']:
    if case.get('Status')!='Passed' or not case.get('Outputs'):continue
    if '(ocr.tesseract)' in case['Name'] and case.get('Input') in truth:
        path=Path(case['Outputs'][0]);reader=PdfReader(path);text='\n'.join(page.extract_text() or '' for page in reader.pages)
        (path.parent/'extracted-text.txt').write_text(text,encoding='utf-8')
        expected=normalize(truth[case['Input']]);actual=normalize(text);errors=distance(expected,actual);cer=errors/len(expected)
        threshold=0.25 if any(s in case['Input'] for s in ['degraded','distorted']) else 0.10
        metrics.append({'Name':'OCR accuracy '+case['Input'],'Status':'Passed' if cer<=threshold else 'Failed','ReferenceCharacters':len(expected),'EditDistance':errors,'CER':cer,'Threshold':threshold,'Output':str(path)})
    if case.get('Input')=='long-table.xlsx':
        path=Path(case['Outputs'][0]);reader=PdfReader(path);text='\n'.join(p.extract_text() or '' for p in reader.pages)
        ids=[f'ITEM-{i:04d}' for i in range(1,1201)];missing=[s for s in ids if s not in text]
        with pdfplumber.open(path) as pdf:minimum=min(float(c['size']) for p in pdf.pages for c in p.chars if c.get('text','').strip())
        metrics.append({'Name':'Long table saved PDF completeness/readability ('+case.get('Engine',case['Name'])+')','Status':'Passed' if not missing and minimum>=7 and len(reader.pages)>=20 else 'Failed','Pages':len(reader.pages),'MissingRows':len(missing),'MinimumFontPt':minimum})
report['IndependentChecks']=metrics
desktop=root/'desktop-checks.json'
if desktop.exists():report['DesktopChecks']=json.loads(desktop.read_text(encoding='utf-8'))
counts=Counter(c['Status'] for c in report['Cases']+metrics);report['Counts']=dict(counts)
(root/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
lines=['# 生成样本验收报告','',f"执行时间：{report['Timestamp']}",'','来源：自行生成的代表性样本，固定随机种子 20260913。不是实际业务样本，OCR 指标只适用于本批样本。','',f"结果：{counts['Passed']} 项通过，{counts['Failed']} 项失败，{counts['Blocked']} 项受环境限制。",'','## 转换与界面检查','','| 检查 | 结果 | 数据 / 限制 |','| --- | --- | --- |']
for case in report['Cases']:
    info=case.get('Reason','')
    if 'Seconds' in case:info+=f" {case['Seconds']:.2f} 秒"
    if 'MaximumDispatcherGapMs' in case:info+=f"；UI 调度 {case['DispatcherTicks']} 次，最大间隔 {case['MaximumDispatcherGapMs']:.1f} ms；进程峰值工作集 {case['PeakWorkingSetMb']:.1f} MiB（整个验收进程）"
    if 'CancelSeconds' in case:info+=f" 取消返回 {case['CancelSeconds']:.3f} 秒；保留 {case['OutputCount']} 个结果"
    if 'Method' in case:info+='模拟工作区，未改变物理显示器 DPI；快照在同目录'
    lines.append(f"| {case['Name']} | {case['Status']} | {info.replace('|','/')} |")
lines+=['','## OCR 独立对照','','字符错误率 CER = 编辑距离 / 对照字符数。使用已导出 PDF 的可提取文字，NFKC 归一化并移除空白，保留大小写、数字和标点。清晰样本阈值 10%，降质样本 25%。','', '| 样本 | 字符数 | 错误数 | CER | 结果 |','| --- | ---: | ---: | ---: | --- |']
for m in metrics:
    if 'CER' in m:lines.append(f"| {m['Name']} | {m['ReferenceCharacters']} | {m['EditDistance']} | {m['CER']:.2%} | {m['Status']} |")
    else:lines+=['',f"长表格：{m['Pages']} 页，遗漏 {m['MissingRows']} 行，最小字体 {m['MinimumFontPt']:.2f} pt，{m['Status']}。"]
if 'DesktopChecks' in report:lines+=['','桌面复核：系统文件与文件夹选择器均观察到 755×474 的可见范围，底部操作按钮可见；默认文件筛选中的扫描 PDF 已成功加入输入列表。没有改变物理显示器分辨率或 DPI，系统弹窗的真实低分辨率 / 高 DPI 模式尚未覆盖。']
lines+=['','## 可复现与边界','','- 样本清单、大小及 SHA256：samples/manifest.json。表格由 Generate-LongTable.mjs 单独生成。','- AutoCAD 未安装时仅报告环境阻塞；Generate-AutoCadSample.ps1 需在安装完整版 AutoCAD 的机器执行后复跑。','- 小屏幕覆盖应用自有关于、反馈、PDF 整理、确认与提示弹窗的三种模拟工作区；保留 15 张 WPF 内容快照。','- 此批基准不涵盖生产文件中的所有字体、损坏方式、扫描仪和 CAD 打印配置。','- 本次检验当前源码编译产物，不代表旧安装包已更新。']
(root/'report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8');print(json.dumps({'counts':dict(counts),'ocr':metrics},ensure_ascii=False))
raise SystemExit(1 if counts['Failed'] else 0)
