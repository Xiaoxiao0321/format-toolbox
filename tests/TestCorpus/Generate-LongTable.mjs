import fs from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { Workbook, SpreadsheetFile } from '@oai/artifact-tool';
const output = path.resolve(process.argv[2] ?? 'artifacts/acceptance/generated/samples');
await fs.mkdir(output,{recursive:true});
const wb = Workbook.create(); const sheet = wb.worksheets.add('LongTable');
sheet.getRange('A1:H1201').values = [['Row ID','Item','Quantity','Unit price','Amount','Batch','Owner','Notes'],...Array.from({length:1200},(_,i)=>[i+1,`ITEM-${String(i+1).padStart(4,'0')}`,i%20+1,25.5,null,`B${i%12+1}`,'Acceptance','Generated sample'])];
sheet.getRange('E2').formulas=[['=C2*D2']];sheet.getRange('E2:E1201').fillDown();
sheet.getRange('A1:H1201').format.font={name:'Arial',size:11};
sheet.getRange('A1:H1201').format.rowHeight=22;
for(const [i,width] of [10,16,12,14,14,10,16,24].entries()) sheet.getRange(`${String.fromCharCode(65+i)}1:${String.fromCharCode(65+i)}1201`).format.columnWidth=width;
sheet.getRange('A1:H1').format={fill:'#243D56',font:{name:'Arial',size:11,bold:true,color:'#FFFFFF'},horizontalAlignment:'center'};
sheet.getRange('C2:E1201').setNumberFormat('0.00');
sheet.freezePanes.freezeRows(1);sheet.showGridLines=false;wb.recalculate();
console.log((await wb.inspect({kind:'table',range:'LongTable!A1199:H1201',include:'values,formulas',tableMaxRows:3,tableMaxCols:8})).ndjson);
const image=await wb.render({sheetName:'LongTable',range:'A1:H12',scale:1.5,format:'png'});
await fs.writeFile(path.join(output,'long-table-preview.png'),new Uint8Array(await image.arrayBuffer()));
await (await SpreadsheetFile.exportXlsx(wb)).save(path.join(output,'long-table.xlsx'));
const manifestPath=path.join(output,'manifest.json');
try {
  const manifest=JSON.parse(await fs.readFile(manifestPath,'utf8'));
  for(const name of ['long-table.xlsx','long-table-preview.png']){
    const bytes=await fs.readFile(path.join(output,name));manifest.files=manifest.files.filter(f=>f.name!==name);
    manifest.files.push({name,bytes:bytes.length,sha256:createHash('sha256').update(bytes).digest('hex')});
  }
  await fs.writeFile(manifestPath,JSON.stringify(manifest,null,2));
} catch(error) { if(error.code!=='ENOENT')throw error; }
