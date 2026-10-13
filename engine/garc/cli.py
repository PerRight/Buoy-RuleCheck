"""명령행.

  python -m garc serve [--port 8765]          AutoCAD 플러그인용 로컬 엔진 서버
  python -m garc check <file.dxf|model.json>  한 도면 판정
  python -m garc revs <rev0.dxf> <rev1.dxf> … 순서대로 넣어 revision diff·상태 변화 확인
  python -m garc report <file…> [-o out.html]  검토 리포트(HTML) — 여러 파일이면 revision 이력 포함
  python -m garc rules                         규칙 목록
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from .contract import raw_to_model, validate_model
from .engine import all_rules, evaluate, OVERALL_KO, STATUS_KO, DISCLAIMER_KO
from .session import Session


def load_any(path: str) -> dict[str, Any]:
    p = Path(path)
    if p.suffix.lower() == ".dxf":
        from .adapters.dxf import read_dxf
        return raw_to_model(read_dxf(p))
    data = json.loads(p.read_text(encoding="utf-8"))
    if data.get("schema", "").startswith("garc.raw"):
        return raw_to_model(data)
    validate_model(data)
    return data


def _print_report(rep: dict[str, Any]) -> None:
    for r in rep["results"]:
        print(f"[{r['status'].upper():6}] {r['rule_id']:16} {r['title_ko']}")
        print(f"         {r['message_ko']}")
        if r.get("suggestion_ko"):
            print(f"         제안: {r['suggestion_ko']}")
        if r.get("targets"):
            print(f"         대상 handle: {', '.join(r['targets'])}")
    print(f"-- 전체: {rep['overall'].upper()} — {rep['overall_ko']}")
    print(f"   ※ {DISCLAIMER_KO}")


def main(argv: list[str] | None = None) -> int:
    # cp949 콘솔·파이프에서 '—'·'→'·'⚠' 같은 문자로 죽지 않게 — 인코딩은 그대로 두고 못 쓰는 문자만 '?'로.
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(prog="garc")
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("serve"); s.add_argument("--port", type=int, default=8765)
    c = sub.add_parser("check"); c.add_argument("path"); c.add_argument("--json", action="store_true")
    v = sub.add_parser("revs"); v.add_argument("paths", nargs="+")
    rp = sub.add_parser("report"); rp.add_argument("paths", nargs="+"); rp.add_argument("-o", "--out", default="ga_report.html")
    sub.add_parser("rules")
    a = ap.parse_args(argv)

    if a.cmd == "serve":
        from .server import serve
        serve(a.port)
    elif a.cmd == "check":
        rep = evaluate(load_any(a.path)).to_dict()
        if a.json:
            print(json.dumps(rep, ensure_ascii=False, indent=2, default=str))
        else:
            _print_report(rep)
        return {"pass": 0, "fail": 1}.get(rep["overall"], 2)
    elif a.cmd == "revs":
        sess = Session()
        for p in a.paths:
            out = sess.check(load_any(p))
            d = out["diff"]
            print(f"=== rev {out['revision']}: {Path(p).name}"
                  + (f"  (rev {out['same_as_revision']}와 동일 — Undo 상태)" if out["same_as_revision"] else ""))
            if not d["initial"]:
                print(f"    변경: 추가 {d['added']} 삭제 {d['removed']} 수정 {d['modified']}"
                      f" 선박정보 {d['ship_changed']} 흘수조건 {'변경' if d['conditions_changed'] else '-'}")
            print(f"    영향 규칙: {out['affected_rules']}  재계산: {out['rerun_rules']}  캐시: {out['cached_rules']}")
            for t in out["transitions"]:
                print(f"    ⚠ {t['rule_id']}: {STATUS_KO.get(t['from'], t['from'])} → {STATUS_KO[t['to']]} ({t['kind']})")
            for r in out["results"]:
                print(f"    [{r['status'].upper():6}] {r['rule_id']:16} {r['message_ko']}")
            print(f"    전체: {OVERALL_KO[out['overall']]}")
    elif a.cmd == "report":
        from .report import build_from_session
        sess = Session()
        for p in a.paths:
            sess.check(load_any(p))
        Path(a.out).write_text(build_from_session(sess, Path(a.paths[-1]).name), encoding="utf-8")
        print(f"리포트 작성: {a.out}")
    elif a.cmd == "rules":
        for r in all_rules():
            print(f"{r.id:16} {r.title_ko}  ({r.ref}; {r.edition})")
            for lim in r.limitations:
                print(f"    - 미검토: {lim}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
