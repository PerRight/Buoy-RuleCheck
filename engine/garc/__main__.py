"""옛 명령 `python -m garc …` → `python -m brc …`와 같다."""
from brc.cli import main

raise SystemExit(main())
