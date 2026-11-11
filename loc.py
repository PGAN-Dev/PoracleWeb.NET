import json,re,glob,os
tr={
'en':"We couldn't reach the server just now. You're still signed in; we'll keep trying.",
'de':"Der Server war gerade nicht erreichbar. Du bist weiterhin angemeldet; wir versuchen es weiter.",
'fr':"Le serveur est injoignable pour le moment. Vous restez connecté(e) ; nous réessayons.",
'es':"No hemos podido contactar con el servidor. Sigues con la sesión iniciada; seguiremos intentándolo.",
'it':"Il server non è raggiungibile al momento. Sei ancora connesso/a; continueremo a riprovare.",
'nl':"De server was even niet bereikbaar. Je bent nog ingelogd; we blijven het proberen.",
'pl':"Nie udało się połączyć z serwerem. Nadal jesteś zalogowany; będziemy próbować dalej.",
'pt':"Não foi possível contactar o servidor agora. Continua com a sessão iniciada; vamos continuar a tentar.",
'pt-BR':"Não foi possível falar com o servidor agora. Você continua conectado; vamos continuar tentando.",
'da':"Vi kunne ikke få forbindelse til serveren lige nu. Du er stadig logget ind; vi prøver igen.",
'sv':"Vi kunde inte nå servern just nu. Du är fortfarande inloggad; vi fortsätter försöka.",
}
for f in glob.glob('src/assets/i18n/*.json'):
    k=os.path.basename(f)[:-5]
    raw=open(f,encoding='utf-8',newline='').read()
    m=re.search(r'("ERROR": \{\r?\n)([ \t]+)',raw)
    assert m,f
    nl='\r\n' if '\r\n' in raw else '\n'
    raw=raw[:m.start()]+m.group(1)+m.group(2)+'"SESSION_UNAVAILABLE": '+json.dumps(tr[k],ensure_ascii=False)+','+nl+m.group(2)+raw[m.end():]
    json.loads(raw)
    open(f,'w',encoding='utf-8',newline='').write(raw)
