"""옛 이름 garc → 새 패키지 brc (2026-10-26 이름 변경: Buoy-RuleCheck).

`python -m garc serve`, `import garc.contract` 같은 옛 명령·스크립트가 그대로 돌게, garc.X를 brc.X 모듈 그 자체로 넘긴다
(같은 모듈 객체 — 서버 세션·클래스가 두 벌 생기지 않는다). 새 코드는 brc를 쓴다.
"""
import importlib
import importlib.abc
import importlib.util
import sys

import brc as _brc
from brc import *  # noqa: F401,F403
from brc import __all__, __version__  # noqa: F401


class _Alias(importlib.abc.MetaPathFinder, importlib.abc.Loader):
    def find_spec(self, fullname, path=None, target=None):
        if fullname.startswith("garc.") and fullname != "garc.__main__":   # `python -m garc`는 이 폴더의 __main__.py
            return importlib.util.spec_from_loader(fullname, self)
        return None

    def create_module(self, spec):
        return importlib.import_module("brc." + spec.name[len("garc."):])

    def exec_module(self, module):
        sys.modules[module.__spec__.name if module.__spec__ else module.__name__] = module


if not any(isinstance(f, _Alias) for f in sys.meta_path):
    sys.meta_path.insert(0, _Alias())
