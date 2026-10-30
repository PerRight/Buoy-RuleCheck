"""외부 생성형 AI 호출 — 엔진에서 PC 밖으로 나가는 요청은 이 파일 하나뿐이다.

2026-10-30 팀 결정(docs/ai-explain.md): 설명 문장만 외부 LLM(Claude API)에 맡긴다.
  · 보내는 본문은 explain.request_body()가 만든다 — 규칙 조항·판정 상태·고칠 방향과 빈칸 이름만.
    도면 좌표·치수·흘수 조건 이름·handle·레이어·블록 이름·선명은 들어가지 않는다(테스트로 확인).
  · 주소는 아래 ENDPOINT 하나로 고정한다. 리다이렉트는 따라가지 않는다(다른 곳으로 본문이 가지 않게).
  · 키는 요청마다 플러그인이 넘기거나(BRCAI에서 저장, 헤더 X-BRC-AI-Key) 환경변수 BRC_AI_KEY / ANTHROPIC_API_KEY.
    키가 없거나, 인터넷이 안 되거나, 답이 이상하면 호출한 쪽이 기본 설명(템플릿)으로 돌아간다.
  · 기본은 꺼짐: 플러그인이 mode="ai"로 부를 때만 이 함수가 쓰인다.
표준 라이브러리(urllib)만 쓴다. 회사 프록시는 환경변수(HTTPS_PROXY)를 따른다.
"""
from __future__ import annotations

import http.client
import json
import os
import re
import socket
import time
import urllib.error
import urllib.request
from typing import Any
from urllib.parse import urlsplit

ENDPOINT = "https://api.anthropic.com/v1/messages"
HOST = "api.anthropic.com"
# Messages API 버전 머리글 — platform.claude.com/docs/en/api/versioning(2026-10-30 확인)의 최신 값.
# 이 버전부터 temperature·top_p·top_k를 보내면 400이므로 본문에 넣지 않는다.
API_VERSION = "2026-10-21"
DEFAULT_MODEL = "claude-haiku-5-5"     # 가장 빠르고 싼 모델(설명 문장 몇 줄이면 충분). BRC_AI_MODEL로 바꿀 수 있음
TIMEOUT_S = 20.0
MAX_ANSWER_BYTES = 200_000
_MODEL_RE = re.compile(r"claude-[a-z0-9][a-z0-9.-]{0,60}", re.ASCII)
_KEY_RE = re.compile(r"[A-Za-z0-9_\-]{20,300}", re.ASCII)


class AiUnavailable(Exception):
    """AI 설명을 못 받은 이유(한국어, 화면에 그대로). 받는 쪽은 기본 설명으로 돌아간다."""

    def __init__(self, reason_ko: str):
        super().__init__(reason_ko)
        self.reason_ko = reason_ko


def model_name() -> str:
    m = (os.environ.get("BRC_AI_MODEL") or "").strip()
    return m if _MODEL_RE.fullmatch(m) else DEFAULT_MODEL


def resolve_key(header_key: str | None) -> str | None:
    """요청 헤더(플러그인이 저장한 키) → BRC_AI_KEY → ANTHROPIC_API_KEY. 형식이 이상하면 없는 것으로 본다.
    플러그인이 키를 넘겼는데 모양이 틀리면 환경변수로 넘어가지 않는다(사용자가 고른 키가 아닌 키로 보내지 않게)."""
    if (header_key or "").strip():
        k = header_key.strip()
        return k if _KEY_RE.fullmatch(k) else None
    for k in (os.environ.get("BRC_AI_KEY"), os.environ.get("ANTHROPIC_API_KEY")):
        k = (k or "").strip()
        if k and _KEY_RE.fullmatch(k):
            return k
    return None


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):  # noqa: D401
        raise AiUnavailable("AI 서비스 주소가 다른 곳으로 넘기려 해서 보내지 않았습니다(리다이렉트 거부).")


def _opener() -> urllib.request.OpenerDirector:
    return urllib.request.build_opener(_NoRedirect())


def call(body: dict[str, Any], key: str, timeout: float = TIMEOUT_S) -> str:
    """Messages API를 한 번 부르고 답의 글자만 돌려준다. 실패는 모두 AiUnavailable(이유)로.
    timeout은 전체 마감(연결부터 답을 다 읽을 때까지) — 조금씩 흘려 보내는 응답도 마감을 넘기면 끊는다."""
    if urlsplit(ENDPOINT).hostname != HOST or not ENDPOINT.startswith("https://"):   # 고정 주소 — 바뀌면 보내지 않는다
        raise AiUnavailable("AI 서비스 주소가 허용 목록과 다릅니다.")
    data = json.dumps(body, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(ENDPOINT, data=data, method="POST", headers={
        "content-type": "application/json",
        "anthropic-version": API_VERSION,
        "x-api-key": key,
    })
    deadline = time.monotonic() + timeout
    try:
        with _opener().open(req, timeout=timeout) as resp:
            raw = _read_until(resp, deadline)
    except AiUnavailable:
        raise
    except urllib.error.HTTPError as e:
        raise AiUnavailable(_http_reason(e.code)) from None
    except (socket.timeout, TimeoutError):
        raise AiUnavailable("AI 응답이 늦어 기본 설명을 보여 줍니다(시간 초과).") from None
    except urllib.error.URLError as e:
        if isinstance(e.reason, (socket.timeout, TimeoutError)):
            raise AiUnavailable("AI 응답이 늦어 기본 설명을 보여 줍니다(시간 초과).") from None
        raise AiUnavailable("인터넷에 연결되지 않아 기본 설명을 보여 줍니다.") from None
    except OSError:
        raise AiUnavailable("인터넷에 연결되지 않아 기본 설명을 보여 줍니다.") from None
    except (http.client.HTTPException, ValueError):
        raise AiUnavailable("AI 서비스 응답이 HTTP 형식이 아닙니다.") from None
    except Exception:   # 무엇이든 기본 설명으로(판정 화면이 500으로 깨지지 않게)
        raise AiUnavailable("AI 서비스 응답을 처리하지 못했습니다.") from None
    if len(raw) > MAX_ANSWER_BYTES:
        raise AiUnavailable("AI 답이 너무 깁니다.")
    try:
        doc = json.loads(raw.decode("utf-8"))
        texts = [b["text"] for b in doc.get("content", []) if isinstance(b, dict) and b.get("type") == "text"
                 and isinstance(b.get("text"), str)]
    except (ValueError, AttributeError, TypeError, KeyError, RecursionError):
        raise AiUnavailable("AI 답 형식을 읽지 못했습니다.") from None
    text = "".join(texts).strip()
    if not text:
        raise AiUnavailable("AI 답이 비어 있습니다.")
    return text


def _read_until(resp, deadline: float) -> bytes:
    """답을 조각으로 읽으며 전체 마감과 크기 상한을 지킨다."""
    chunks, size = [], 0
    while True:
        if time.monotonic() > deadline:
            raise TimeoutError
        part = resp.read1(16384) if hasattr(resp, "read1") else resp.read(16384)
        if not part:
            return b"".join(chunks)
        chunks.append(part)
        size += len(part)
        if size > MAX_ANSWER_BYTES:
            return b"".join(chunks)


def _http_reason(code: int) -> str:
    if code in (401, 403):
        return "AI 서비스가 키를 받지 않았습니다 — BRCAI에서 API 키를 확인하세요."
    if code == 429:
        return "AI 사용량 한도에 걸렸습니다 — 잠시 뒤 다시 시도하세요."
    if code == 400:
        return "AI 서비스가 요청 형식을 받지 않았습니다(모델 이름·버전 확인)."
    if code >= 500:
        return "AI 서비스 쪽 오류입니다 — 잠시 뒤 다시 시도하세요."
    return f"AI 서비스가 요청을 거절했습니다(HTTP {code})."
