import re,glob
n=0
for p in sorted(glob.glob('*/*-add-dialog.component.html')+glob.glob('*/*-edit-dialog.component.html')):
    s=open(p,encoding='utf-8',newline='').read()
    m=re.search(r'<app-deliver-to-field \[control\]="(\w+)\.controls\.destination"',s)
    if not m: continue
    f=m.group(1)
    nl='\r\n' if '\r\n' in s else '\n'
    pat=re.compile(r'(<app-template-selector(\s+)\[alarmType\]="[^"]+")')
    assert len(pat.findall(s))==1,p
    s=pat.sub(lambda mm: mm.group(1)+mm.group(2)+'[side]="%s.controls.destination.value"'%f,s)
    open(p,'w',encoding='utf-8',newline='').write(s); n+=1; print(p,f)
print(n)
