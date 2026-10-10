import fitz,numpy as np,json
from pathlib import Path
from PIL import Image
from scipy.ndimage import label,find_objects
import sys
p=fitz.open(sys.argv[1] if len(sys.argv)>1 else 'preflop-charts.pdf')
root=Path(__file__).resolve().parents[1]/'PokerCompanion.App'/'Data';(root/'Charts').mkdir(exist_ok=True)
titles=[
['UTG','UTG+1','UTG+2','LJ','HJ','CO','BTN','SB'],
['UTG+1 vs UTG','UTG+2 vs UTG/UTG+1','LJ vs UTG/UTG+1','LJ vs UTG+2','HJ vs UTG','HJ vs UTG+1','HJ vs UTG+2','HJ vs LJ'],
['CO vs UTG/UTG+1','CO vs UTG+2','CO vs LJ','CO vs HJ'],
['BTN vs UTG','BTN vs UTG+1','BTN vs UTG+2','BTN vs LJ','BTN vs HJ','BTN vs CO'],
['SB vs UTG/UTG+1','SB vs UTG+2','SB vs LJ','SB vs HJ','SB vs CO','SB vs BTN'],
['BB vs UTG/UTG+1','BB vs UTG+2','BB vs LJ','BB vs HJ','BB vs CO','BB vs BTN','BB vs SB'],
['UTG vs UTG+1','UTG vs UTG+2','UTG vs LJ','UTG vs HJ','UTG vs CO/BTN','UTG vs SB/BB'],
['UTG+1 vs UTG+2','UTG+1 vs LJ','UTG+1 vs HJ/CO','UTG+1 vs BTN','UTG+1 vs SB/BB'],
['UTG+2 vs LJ','UTG+2 vs HJ','UTG+2 vs CO/BTN','UTG+2 vs SB/BB'],
['LJ vs HJ','LJ vs CO','LJ vs BTN','LJ vs SB','LJ vs BB'],
['HJ vs CO','HJ vs BTN','HJ vs SB','HJ vs BB','CO vs BTN/SB','CO vs BB'],
['BTN vs SB/BB','SB vs BB','SB limp vs BB raise']]
ranks='AKQJT98765432'; charts=[]
for page_idx,names in enumerate(titles,2):
 infos=p[page_idx].get_image_info(xrefs=True)
 # Group rows by vertical separation, then sort left to right.
 infos=sorted(infos,key=lambda x:x['bbox'][1]);rows=[]
 for info in infos:
  if not rows or info['bbox'][1]-rows[-1][0]['bbox'][1]>100:rows.append([])
  rows[-1].append(info)
 infos=[x for row in rows for x in sorted(row,key=lambda x:x['bbox'][0])]
 assert len(infos)==len(names)
 for k,(info,title) in enumerate(zip(infos,names)):
  pix=fitz.Pixmap(p,info['xref']);a=np.frombuffer(pix.samples,np.uint8).reshape(pix.height,pix.width,pix.n)[:,:,:3]
  labs,n=label(a.min(2)<220);rect=[]
  for s in find_objects(labs):
   if s:
    h=s[0].stop-s[0].start;w=s[1].stop-s[1].start
    if 30<w<52 and 30<h<52 and s[0].start<610:rect.append((s[1].start,s[0].start,w,h))
  def clusters(values):
   groups=[]
   for v in sorted(values):
    if not groups or v-groups[-1][0]>20:groups.append([])
    groups[-1].append(v)
   return [int(np.median(v)) for v in groups]
  xs=clusters([q[0] for q in rect]);ys=clusters([q[1] for q in rect])
  assert len(xs)==13 and len(ys)==13,(title,len(xs),len(ys))
  w=int(np.median([q[2] for q in rect]));h=int(np.median([q[3] for q in rect]))
  rect=[(x,y,w,h) for y in ys for x in xs]
  family='RFI' if page_idx==2 else ('Facing raise' if page_idx<=7 else 'Facing 3-bet')
  actions={};counts={}
  for i,(x,y,w,h) in enumerate(rect):
   patch=a[y+3:y+h-3,x+3:x+w-3];r,g,b=np.median(patch.reshape(-1,3),axis=0)
   if r>180 and g<150 and b<150:color='red'
   elif g>100 and g>r*1.4 and g>b*1.3:color='green'
   elif b>130 and b>r*1.4 and b>g*1.3:color='blue'
   elif max(r,g,b)<150:color='gray'
   elif min(r,g,b)>210:color='white'
   else:raise ValueError((title,i,(r,g,b)))
   if family=='RFI': action={'red':'Raise','blue':'Raise as a bluff','green':'Limp','white':'Fold'}[color]
   elif family=='Facing raise':action={'red':'3-bet for value','blue':'3-bet as a bluff','green':'Call','white':'Fold'}[color]
   else:
    prefix='3-bet' if title=='SB limp vs BB raise' else '4-bet'
    action={'red':prefix+' for value','blue':prefix+' as a bluff','green':'Call','gray':'Fold','white':'Not in prior range'}[color]
   row,col=divmod(i,13)
   hand=ranks[row]+ranks[col] if row<=col else ranks[col]+ranks[row]
   if row!=col:hand+='s' if row<col else 'o'
   actions[hand]=action;counts[action]=counts.get(action,0)+(6 if row==col else 4 if row<col else 12)
  assert len(actions)==169 and sum(counts.values())==1326
  name=f'p{page_idx+1:02d}-{k+1:02d}.png';pix.save(str(root/'Charts'/name))
  charts.append({'Id':name[:-4],'Name':family+' | '+title,'Page':page_idx+1,'ImagePath':'Data/Charts/'+name,'Actions':actions})
(root/'charts.json').write_text(json.dumps(charts,indent=2))
print('Extracted',len(charts),'charts;',len(charts)*169,'hand entries')
