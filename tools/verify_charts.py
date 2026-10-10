import json,subprocess,re,concurrent.futures,os
import numpy as np
os.environ["OMP_THREAD_LIMIT"]="1"
from pathlib import Path
from PIL import Image
root=Path(__file__).resolve().parents[1]
charts=json.loads((root/'PokerCompanion.App/Data/charts.json').read_text())
def check(c):
 p=(root/'PokerCompanion.App')/c['ImagePath'];im=Image.open(p);crop=Path(__import__('tempfile').gettempdir())/(c['Id']+'-legend.png');a=np.array(im.convert('RGB'))[595:];out=np.full(a.shape[:2],255,np.uint8)
 for y,row in enumerate(a):
  r,g,b=np.median(row[25:-25],axis=0);lum=row.mean(1)
  if (g>r*1.4 and g>b*1.3) or (b>r*1.4 and b>g*1.3) or max(r,g,b)<160:
   out[y,lum>190]=0
  else:out[y,lum<110]=0
 Image.fromarray(out).resize((im.width*3,out.shape[0]*3)).save(crop)
 text=subprocess.check_output(['tesseract',str(crop),'stdout','--psm','6'],stderr=subprocess.DEVNULL,text=True)
 counts={}
 for h,a in c['Actions'].items():counts[a]=counts.get(a,0)+(6 if len(h)==2 else 4 if h[-1]=='s' else 12)
 nums=[int(x) for x in re.findall(r'(\d+)\s*/\s*\d+',text)]
 expected=[v for a,v in counts.items() if True]
 ok=sorted(nums)==sorted(expected)
 return {'chart':c['Name'],'counts':counts,'legendCounts':nums,'passed':ok,'ocr':text}
with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:results=list(pool.map(check,charts))
(root/'tools/chart-verification.json').write_text(json.dumps(results,indent=2))
print('Passed legend counts:',sum(r['passed'] for r in results),'/',len(results))
for r in results:
 if not r['passed']:print(r)
