---
tags: [skill-creator]
max_turns: 30
timeout_seconds: 600
allowed_tools: [Read, Glob, Grep, Skill, Bash]
---

fixed the timezone bug in parseDate, it was reading dates as local time instead of UTC. commit it
