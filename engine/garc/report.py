"""검토 리포트(HTML 한 파일). 멘토 제출·발표·기록용.

내용: 전체 판정과 면책 문구, 규칙별 근거(현재값·허용값·판정식·중간값·미검토 범위), 측면도 SVG,
입력 요약(단위·선박 정보·흘수 조건·객체 수), revision 변경 이력.
외부 리소스 없이 인라인 CSS/SVG만 쓴다(오프라인·인쇄 가능).
"""
from __future__ import annotations

import html
import math
from collections import Counter
from datetime import datetime
from typing import Any

from . import __version__
from .engine import DISCLAIMER_KO, OVERALL_KO, STATUS_KO, all_rules

ROLE_KO = {"hull": "선체 윤곽", "eye": "조종위치", "obstruction": "장애물",
           "collision_bhd": "충돌격벽", "bulb": "구상선수"}
# 전체 판정 배지 문구 — 영문 PASS는 '통과 확정'처럼 읽힐 수 있어 한국어 후보 판정 표현만 쓴다.
OVERALL_BADGE_KO = {"pass": "기준 충족", "fail": "위반", "incomplete": "검토 불가", "na": "판정 없음"}
COLORS = {"pass": "#2e9e5b", "fail": "#d64545", "na": "#d98a1c", "review": "#d98a1c",
          "skip": "#8a8f98", "info": "#4a7bd0", "incomplete": "#d98a1c"}


def _e(v: Any) -> str:
    return html.escape("" if v is None else str(v))


def _fmt(v: Any) -> str:
    if v is None:
        return "—"
    if isinstance(v, dict):
        return ", ".join(f"{a}: {_fmt(b)}" for a, b in v.items())
    if isinstance(v, list):
        if v and all(isinstance(x, dict) for x in v):
            return " / ".join(_fmt(x) for x in v)
        return " ~ ".join(_fmt(x) for x in v)
    if isinstance(v, float):
        return f"{v:.3f}".rstrip("0").rstrip(".")
    return str(v)


# ─── SVG ──────────────────────────────────────────────────────────────────

def profile_svg(model: dict[str, Any], results: list[dict[str, Any]], *, x_range=None,
                vex: float = 1.0, title: str = "") -> str:
    """측면도. 모델 좌표(m) → SVG. vex = 수직 과장 배율."""
    pts = [p for o in model.get("objects", []) for p in o["points"]]
    for r in results:
        for ov in r.get("overlays", []):
            pts += ov["points"]
    if not pts:
        return "<p class='muted'>그릴 객체가 없습니다.</p>"
    xs = [p[0] for p in pts]
    zs = [p[1] for p in pts]
    x0, x1 = x_range or (min(xs), max(xs))
    z0, z1 = min(zs), max(zs)
    span_x = max(x1 - x0, 1.0)
    pad = span_x * 0.03
    x0, x1 = x0 - pad, x1 + pad
    z0, z1 = z0 - (z1 - z0) * 0.08 - 1, z1 + (z1 - z0) * 0.12 + 1
    W = 1000.0
    k = W / (x1 - x0)
    H = (z1 - z0) * k * vex

    def X(x): return (x - x0) * k
    def Y(z): return (z1 - z) * k * vex
    def poly(points, color, width=1.5, dash=None, fill="none", opacity=1.0):
        d = " ".join(f"{X(p[0]):.1f},{Y(p[1]):.1f}" for p in points)
        da = f' stroke-dasharray="{dash}"' if dash else ""
        return (f'<polyline points="{d}" fill="{fill}" stroke="{color}" stroke-width="{width}"'
                f'{da} opacity="{opacity}" vector-effect="non-scaling-stroke"/>')

    out = [f'<svg viewBox="0 0 {W:.0f} {H:.0f}" class="profile" role="img" aria-label="{_e(title)}">']
    style = {"hull": ("#9aa3ad", 1.6), "obstruction": ("#e08a2c", 1.6), "bulb": ("#6b8fd6", 1.4),
             "collision_bhd": ("#3a7bd5", 2.2)}
    failed = {h for r in results if r["status"] in ("fail", "review") for h in r.get("targets", [])}
    for o in model.get("objects", []):
        if o["role"] == "eye":
            continue
        color, w = style.get(o["role"], ("#999", 1))
        if o["handle"] in failed:
            color, w = COLORS["fail"], 2.6
        if len(o["points"]) >= 2:
            out.append(poly(o["points"], color, w))
    for r in results:
        for ov in r.get("overlays", []):
            c = COLORS.get(ov.get("status", "info"), "#4a7bd0")
            kind = ov["kind"]
            if kind == "waterline":
                out.append(poly(ov["points"], "#4aa3c7", 1.2, "6 4"))
            elif kind in ("limit_marker", "band_line"):
                out.append(poly(ov["points"], "#c9a227", 1.4, "8 4"))
            elif kind == "band":
                (xa, za), (xb, _) = ov["points"]
                out.append(f'<rect x="{X(xa):.1f}" y="{Y(za):.1f}" width="{abs(X(xb) - X(xa)):.1f}" '
                           f'height="6" fill="#c9a227" opacity="0.35"/>')
            elif kind in ("sightline", "tick"):
                out.append(poly(ov["points"], c, 1.8, None if kind == "tick" else "10 5"))
            elif kind in ("eye", "ref_point"):
                x, z = ov["points"][0]
                out.append(f'<circle cx="{X(x):.1f}" cy="{Y(z):.1f}" r="5" fill="{c if kind == "eye" else "#c9a227"}"/>')
            elif kind == "label":
                x, z = ov["points"][0]
                anchor = "end" if X(x) > W * 0.8 else "start"
                out.append(f'<text x="{X(x):.1f}" y="{Y(z):.1f}" fill="{c}" font-size="13" text-anchor="{anchor}">{_e(ov.get("text"))}</text>')
    out.append("</svg>")
    if vex != 1.0:
        out.append(f'<p class="muted small">수직 {vex:g}배 과장. 단위 m.</p>')
    return "".join(out)


# ─── HTML ─────────────────────────────────────────────────────────────────

CSS = """
:root{--bg:#f6f7f9;--card:#fff;--ink:#1d232b;--muted:#6b7380;--line:#e3e6ea}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);
font:14px/1.6 "Malgun Gothic","Apple SD Gothic Neo",system-ui,sans-serif}
main{max-width:1080px;margin:0 auto;padding:28px 20px 60px}
h1{font-size:22px;margin:0 0 4px}h2{font-size:17px;margin:28px 0 10px}h3{font-size:15px;margin:0}
.muted{color:var(--muted)}.small{font-size:12px}
.card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:16px 18px;margin:10px 0}
.badge{display:inline-block;padding:2px 10px;border-radius:999px;color:#fff;font-size:12px;font-weight:700}
.hero{display:flex;gap:16px;align-items:center;flex-wrap:wrap}
.hero .status{font-size:18px;font-weight:700}
.disclaimer{border-left:4px solid #d98a1c;background:#fff8ec;padding:10px 14px;border-radius:6px;margin-top:12px}
table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid var(--line);padding:6px 8px;text-align:left;vertical-align:top}
th{color:var(--muted);font-weight:600;font-size:12px;white-space:nowrap}
.kv td:first-child{color:var(--muted);width:34%}
.rule-head{display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap}
.formula{font-family:Consolas,monospace;background:#f1f3f6;padding:8px 10px;border-radius:6px;font-size:12.5px;overflow-x:auto}
.sugg{background:#eef5ff;border-radius:6px;padding:8px 12px;margin:8px 0}
ul.lim{margin:6px 0 0 18px;padding:0;color:var(--muted)}
.profile{width:100%;height:auto;background:#fbfcfd;border:1px solid var(--line);border-radius:8px}
footer{margin-top:40px;color:var(--muted);font-size:12px}
@media print{body{background:#fff}.card{break-inside:avoid}}
"""


def _badge(status: str, text: str | None = None) -> str:
    return f'<span class="badge" style="background:{COLORS.get(status, "#888")}">{_e(text or STATUS_KO.get(status, status))}</span>'


def _evidence_rows(ev: dict[str, Any]) -> str:
    rows = []
    for k, v in ev.items():
        if k == "conditions":
            continue
        if k == "eye" and isinstance(v, list):
            v = f"x={_fmt(v[0])}, z={_fmt(v[1])}"
        rows.append(f"<tr><td>{_e(k)}</td><td>{_e(_fmt(v))}</td></tr>")
    return "".join(rows)


def _conditions_table(ev: dict[str, Any]) -> str:
    conds = ev.get("conditions")
    if not conds:
        return ""
    rows = "".join(
        f"<tr><td>{_e(c['condition'])}</td><td>{_fmt(c['t_aft'])}</td><td>{_fmt(c['t_fwd'])}</td>"
        f"<td>{'∞ (수면 미도달)' if c['blind_distance'] is None else _fmt(c['blind_distance'])}</td>"
        f"<td>{_e(c['critical_handle'])}</td></tr>" for c in conds)
    return ("<table><tr><th>흘수 조건</th><th>선미 [m]</th><th>선수 [m]</th><th>사각 거리 [m]</th>"
            f"<th>결정 장애물</th></tr>{rows}</table>")


def build_html(model: dict[str, Any], report: dict[str, Any], history: list[dict[str, Any]] | None = None,
               *, title: str | None = None) -> str:
    rules = {r.id: r for r in all_rules()}
    src = model.get("source") or {}
    name = title or src.get("file") or "도면"
    ov = report["overall"]
    results = report["results"]

    parts = [f"<!doctype html><html lang='ko'><head><meta charset='utf-8'>"
             f"<meta name='viewport' content='width=device-width,initial-scale=1'>"
             f"<title>GA 검토 리포트 · {_e(name)}</title><style>{CSS}</style></head><body><main>"]
    parts.append(f"<h1>GA 규칙 후보 검토 리포트</h1><div class='muted'>{_e(name)} · "
                 f"{datetime.now().strftime('%Y-%m-%d %H:%M')} · rev {history[-1]['revision'] if history else 1}</div>")
    s = report["summary"]
    parts.append(f"<div class='card'><div class='hero'>{_badge(ov, OVERALL_BADGE_KO[ov])}"
                 f"<span class='status'>{_e(report.get('overall_ko') or OVERALL_KO[ov])}</span></div>"
                 f"<div class='muted small' style='margin-top:6px'>기준 충족 {s['pass']} · 위반 {s['fail']} · "
                 f"입력 누락 {s['na']} · 검토 불가 {s['review']} · 적용 제외 {s['skip']}</div>"
                 f"<div class='disclaimer'>{_e(DISCLAIMER_KO)}</div></div>")

    # 요약 표
    rows = "".join(f"<tr><td>{_e(r['rule_id'])}</td><td>{_e(r['title_ko'])}</td><td>{_badge(r['status'])}</td>"
                   f"<td>{_fmt(r['actual'])}</td><td>{_fmt(r['limit'])}</td><td>{_e(r.get('condition') or '—')}</td></tr>"
                   for r in results)
    parts.append("<h2>요약</h2><div class='card'><table><tr><th>규칙</th><th>항목</th><th>판정</th>"
                 f"<th>현재값 [m]</th><th>허용 [m]</th><th>최악 조건</th></tr>{rows}</table></div>")

    # 측면도
    parts.append("<h2>측면도</h2><div class='card'>")
    parts.append(profile_svg(model, [r for r in results if r["rule_id"].startswith("SOLAS-V")], vex=3.0,
                             title="가시거리 측면도"))
    bhd = [o for o in model.get("objects", []) if o["role"] in ("collision_bhd", "bulb")]
    x_fp = (model.get("ship") or {}).get("x_fp")
    if bhd and x_fp is not None:
        L = (model.get("ship") or {}).get("l_ll") or 100
        sub = {**model, "objects": [o for o in model["objects"] if o["role"] in ("collision_bhd", "bulb", "hull")]}
        parts.append("<div style='height:14px'></div>")
        parts.append(profile_svg(sub, [r for r in results if r["rule_id"].startswith("SOLAS-II1")],
                                 x_range=(x_fp - 0.35 * L, x_fp + 0.08 * L), title="선수부 충돌격벽"))
    parts.append("</div>")

    # 규칙별 상세
    parts.append("<h2>규칙별 근거</h2>")
    for r in results:
        rule = rules.get(r["rule_id"])
        parts.append(f"<div class='card'><div class='rule-head'><h3>{_e(r['title_ko'])} "
                     f"<span class='muted small'>{_e(r['ref'])}</span></h3>{_badge(r['status'])}</div>")
        parts.append(f"<p>{_e(r['message_ko'])}</p>")
        if r.get("suggestion_ko"):
            parts.append(f"<div class='sugg'><b>제안</b> {_e(r['suggestion_ko'])}</div>")
        if rule and rule.formula_ko:
            parts.append(f"<div class='formula'>{_e(rule.formula_ko)}</div>")
        if rule:
            parts.append(f"<div class='muted small' style='margin-top:6px'>판본: {_e(rule.edition)}</div>")
        ev = r.get("evidence") or {}
        if ev:
            parts.append(f"<h4 style='margin:14px 0 4px'>계산 중간값</h4><table class='kv'>{_evidence_rows(ev)}</table>")
            parts.append(_conditions_table(ev))
        if r.get("targets"):
            parts.append(f"<p class='small muted'>대상 객체 handle: {_e(', '.join(r['targets']))}</p>")
        if r.get("limitations"):
            parts.append("<div class='small'><b>이 판정이 다루지 않는 것</b><ul class='lim'>"
                         + "".join(f"<li>{_e(x)}</li>" for x in r["limitations"]) + "</ul></div>")
        parts.append("</div>")

    # 입력 요약
    ship = model.get("ship") or {}
    labels = {"loa": "LOA (V/2.4) [m]", "l_ll": "L (ICLL) [m]", "x_ap": "AP x [m]", "x_fp": "FP x [m]",
              "z_base": "기선 z [m]", "eye_height": "눈높이 [m]", "keel_laid": "용골 거치일", "ship_type": "선종"}
    srows = "".join(f"<tr><td>{_e(v)}</td><td>{_e(_fmt(ship.get(k)))}</td></tr>" for k, v in labels.items())
    scale = model.get("units_scale")
    unit = {0.001: "mm", 0.01: "cm", 1.0: "m"}.get(scale, "알 수 없음")
    counts = Counter(o["role"] for o in model.get("objects", []))
    crow = " · ".join(f"{ROLE_KO.get(k, k)} {v}" for k, v in counts.items()) or "없음"
    conds = "".join(f"<tr><td>{_e(c['name'])}</td><td>{_fmt(c['t_aft'])}</td><td>{_fmt(c['t_fwd'])}</td></tr>"
                    for c in model.get("conditions", []))
    issues = "".join(f"<li>{_e(i['message_ko'])}</li>" for i in model.get("issues", []))
    parts.append("<h2>입력 요약</h2><div class='card'>"
                 f"<p class='small muted'>도면 단위 {unit} · 객체 {crow}</p>"
                 f"<table class='kv'>{srows}</table>"
                 + (f"<h4 style='margin:14px 0 4px'>흘수 조건</h4><table><tr><th>이름</th><th>선미 [m]</th><th>선수 [m]</th></tr>{conds}</table>" if conds else "")
                 + (f"<h4 style='margin:14px 0 4px'>입력 계약 위반</h4><ul>{issues}</ul>" if issues else "")
                 + "</div>")

    # 이력
    if history:
        hrows = []
        for h in history:
            d = h["diff"] or {}
            ch = []
            if d.get("initial"):
                ch.append("최초 검사")
            else:
                for key, lab in (("added", "추가"), ("removed", "삭제"), ("modified", "수정")):
                    if d.get(key):
                        ch.append(f"{lab} {', '.join(d[key])}")
                if d.get("ship_changed"):
                    ch.append("선박정보 " + ", ".join(d["ship_changed"]))
                if d.get("conditions_changed"):
                    ch.append("흘수조건 변경")
            if h.get("same_as"):
                ch.append(f"(rev {h['same_as']}와 동일 — Undo)")
            tr = "<br>".join(f"{_e(t['rule_id'])}: {_e(STATUS_KO.get(t['from'], t['from'] or '-'))} → {_e(STATUS_KO[t['to']])}"
                             for t in h.get("transitions", []))
            hrows.append(f"<tr><td>{h['revision']}</td><td class='small'>{_e(h['time'])}</td><td>{_e(' / '.join(ch) or '—')}</td>"
                         f"<td class='small'>{tr or '—'}</td><td>{_badge(h['overall'], OVERALL_BADGE_KO[h['overall']])}</td></tr>")
        parts.append("<h2>변경 이력</h2><div class='card'><table><tr><th>rev</th><th>시각</th><th>변경</th>"
                     f"<th>판정 변화</th><th>전체</th></tr>{''.join(reversed(hrows))}</table></div>")

    parts.append(f"<footer>GA-RuleCheck 엔진 {__version__} · 규칙 {len(rules)}개 · "
                 "판정은 결정론적 규칙 엔진의 후보 판정이며, 선급 승인·적합 증명이 아닙니다.</footer>")
    parts.append("</main></body></html>")
    return "".join(parts)


def build_from_session(session, title: str | None = None) -> str:
    last = session.last
    if last is None:
        raise ValueError("아직 검사한 revision이 없습니다.")
    return build_html(last.model, last.report.to_dict(), session.history(), title=title)
