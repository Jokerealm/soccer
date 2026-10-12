#!/usr/bin/env python3
"""Check the actual login service over HTTP; never log credential payloads."""
import argparse,json
from pathlib import Path
from urllib.request import Request,urlopen
from urllib.error import HTTPError
p=argparse.ArgumentParser();p.add_argument('--url',default='http://127.0.0.1:27862');p.add_argument('--report',type=Path,default=Path('reports/usability-018-login-service.json'));args=p.parse_args()
checks=[]
def call(path,payload=None):
 req=Request(args.url+path,data=None if payload is None else json.dumps(payload).encode(),headers={'Content-Type':'application/json'})
 try:
  with urlopen(req,timeout=5) as r:return r.status,json.loads(r.read())
 except HTTPError as e:
  try: body=json.loads(e.read())
  except ValueError: body={}
  return e.code,body
def check(name,condition):
 checks.append({'name':name,'passed':bool(condition)})
 print(('PASS ' if condition else 'FAIL ')+name)
status,body=call('/health');check('service advertises expected identity',status==200 and body.get('service')=='SkillSoccer.Login')
for account in ['a','b']:
 status,body=call('/api/login',{'account':account,'password':account});check('server accepts debug account '+account,status==200 and body.get('accountId')==account and body.get('service')=='SkillSoccer.Login')
for account,password in [('a','incorrect'),('b','a'),('c','c'),('a',''),('a','x'*65)]:
 status,body=call('/api/login',{'account':account,'password':password});check('server rejects invalid credentials case '+str(len(checks)),status==401 and 'accountId' not in body)
status,body=call('/api/login',{});check('server rejects missing credential fields',status==401)
args.report.parent.mkdir(parents=True,exist_ok=True);args.report.write_text(json.dumps({'passed':all(c['passed'] for c in checks),'total':len(checks),'checks':checks},ensure_ascii=False,indent=2)+'\n')
raise SystemExit(0 if all(c['passed'] for c in checks) else 1)
