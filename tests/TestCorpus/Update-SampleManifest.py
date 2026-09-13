from pathlib import Path
import hashlib, json, argparse
parser=argparse.ArgumentParser();parser.add_argument('--directory',default='artifacts/acceptance/generated/samples');args=parser.parse_args();root=Path(args.directory).resolve()
manifest=json.loads((root/'manifest.json').read_text(encoding='utf-8'))
manifest['files']=[]
for path in sorted(root.iterdir()):
    if path.is_file() and path.name!='manifest.json' and not path.name.endswith('.inspect.ndjson'):
        with path.open('rb') as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest()
        manifest['files'].append({'name':path.name,'bytes':path.stat().st_size,'sha256':digest})
(root/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print(f"Manifest refreshed: {len(manifest['files'])} files")
