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
        if not fullname.startswith("garc.") or fullname == "garc.__main__":   # `python -m garc`는 이 폴더의 __main__.py
            return None
        target_name = "brc." + fullname[len("garc."):]
        try:
            if importlib.util.find_spec(target_name) is None:   # 없는 모듈은 옛 이름 그대로 'No module named garc.…'
                return None
        except ModuleNotFoundError:
            return None
        return importlib.util.spec_from_loader(fullname, self)

    def create_module(self, spec):
        module = importlib.import_module("brc." + spec.name[len("garc."):])
        spec.loader_state = module.__spec__   # import 장치가 __spec__을 garc.X로 덮어쓰므로 원래 것을 기억했다가 되돌린다
        return module

    def exec_module(self, module):
        # 같은 모듈 객체를 garc.X 이름으로도 등록만 한다(import 장치가 sys.modules에 넣는다). brc.X의 __spec__은 그대로 둔다
        # — 그래야 importlib.reload(brc.X)·pickle 등이 원래 이름으로 동작한다(2026-10-26 Sonnet 검증 지적).
        alias = module.__spec__
        if alias is not None and getattr(alias, "loader_state", None) is not None:
            module.__spec__ = alias.loader_state


if not any(isinstance(f, _Alias) for f in sys.meta_path):
    sys.meta_path.insert(0, _Alias())
