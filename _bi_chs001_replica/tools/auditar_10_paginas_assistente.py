import json, pathlib, collections
root=pathlib.Path(r'C:\Dev\N-A-Connect aa\N-A-Connect')
source=pathlib.Path(r'F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages')
generated=root/'ClientApp/src/features/production/biChs001/generated'
out=root/'_bi_chs001_replica/AUDITORIA_ASSISTENTE_10_PAGINAS.md'
names=['Resumo 30%','Resumo 60%','Resumo 90%','LDP - Engineering - 30%','LDP - Engineering - 60%','LDP - Engineering - 90%','LDP - 3D - 30%','LDP - 3D - 60%','LDP - 3D - 90%','Programação']
order=json.loads((source/'pages.json').read_text(encoding='utf-8'))['pageOrder']
pages=[(i+1,k,json.loads((source/k/'page.json').read_text(encoding='utf-8'))) for i,k in enumerate(order)]
pages=sorted([x for x in pages if x[2].get('displayName') in names],key=lambda x:names.index(x[2]['displayName']))
def refs(node,keys):
    if isinstance(node,dict):
        for k,v in node.items():
            if k in ('Measure','Column') and isinstance(v,dict) and v.get('Property'): keys.add((k,v['Property']))
            refs(v,keys)
    elif isinstance(node,list):
        for v in node: refs(v,keys)
lines=['# Auditoria independente: 10 páginas prioritárias (referência estrutural PBIR)','', 'Leitura somente: nenhuma alteração em PBIX, banco de dados, frontend, ou trabalho do Claude.', '', 'Limitação: comparar IDs, coordenadas, tipos e referências de campo NÃO é confirmação de paridade visual e numérica; DAX precisa ser validado contra o modelo do Power BI.','']
for idx,pageid,pj in pages:
    name=pj['displayName'];orig=[]
    for p in (source/pageid/'visuals').glob('*/visual.json'):
        orig.append(json.loads(p.read_text(encoding='utf-8')))
    genp=generated/('p%02d.json'%idx)
    obj=json.loads(genp.read_text(encoding='utf-8')) if genp.exists() else {}
    gen=obj.get('visuais',[])
    a={v.get('name') for v in orig};b={v.get('id') for v in gen}
    types=collections.Counter((v.get('visual') or {}).get('visualType','group' if 'visualGroup' in v else 'desconhecido') for v in orig)
    fields=set()
    for v in orig: refs(v,fields)
    byid={v.get('id'):v for v in gen};dif=[]
    for v in orig:
        g=byid.get(v.get('name'))
        if g is None: continue
        pos=v.get('position') or {}
        for key,field in [('x','x'),('y','y'),('width','w'),('height','h')]:
            x=pos.get(key,0);y=g.get(field,0)
            if isinstance(x,(int,float)) and isinstance(y,(int,float)) and abs(x-y)>0.015: dif.append((v.get('name'),key,x,y))
    lines.extend(['## '+name,'','- Ordem original: '+str(idx)+'; ID: '+pageid+'; resolução: '+str(pj.get('width'))+'x'+str(pj.get('height'))+'.','- Visuais fonte: '+str(len(orig))+'; extraídos: '+str(len(gen))+'.','- IDs ausentes no gerado: '+str(len(a-b))+'; extras: '+str(len(b-a))+'; coordenadas diferentes: '+str(len(dif))+'.','- Tipos: '+', '.join(k+' ('+str(n)+')' for k,n in sorted(types.items()))+'.','- Campos e medidas: '+('; '.join(k+': '+p for k,p in sorted(fields)[:35]) if fields else 'sem referências no JSON')+'.',''])
    if a-b:lines.append('- IDs faltando: '+', '.join(sorted(a-b)[:10]))
    if dif:lines.append('- Posições divergentes (amostra): '+str(dif[:4]))
lines.extend(['---','','Próxima validação obrigatória: capturas do PBIX e da aplicação na mesma dimensão, elementos textuais e recursos, filtros/medidas/relacionamentos DAX e dados reais CHS001; não atestar idêntico apenas pelo JSON.'])
out.write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('RELATORIO:',out)
print('PAGINAS:',len(pages))
print('\n'.join(l for l in lines if l.startswith('## ') or l.startswith('- Visuais fonte:') or l.startswith('- IDs ausentes')))
