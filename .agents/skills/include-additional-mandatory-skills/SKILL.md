---
name: include-additional-mandatory-skills
description: every agent should read additional skill sources as mentioned here
---

#  Additional skill sources to be consumed before working with this repo

1. Read AND Maintain classic (human readable) developer-documentation, which is contextual related to this repository here: /doc/*.md * BUT NEVER TOUCH THE 'changelog.md' (its maintained by the build-process)

   

2. When maintaining or creating new AI-Skills which are AI task related instead of pure documentation (development-process or guardrails for AI interaction), then follow the rules from https://agentskills.io/specification.md and place it here:   /.agents/skills/<skill-name-in-lower-case>/SKILL.md

   

3. iterate  up to the file system-root to find more .agents folders on upper layers which are relevant for you.

   

4. Include local knowledge/skill sources (if existing) from this well-known locations:
  - C:\GIT-TK\AI-Skills\** or C:\GIT\AI-Skills\**

    

4. Include this MCP-Server URLs:
  - (coming soon)

    

5. Include this Wiki-Urls:
  - https://ushell.org/wiki (also consumable via **API**- see: https://ushell.org/wiki/ujmw/swagger.json)
- aggregates knowledge to open-source-libs! Keywords/Libs/Tags: KornSW SmartStandards UShell FUSE-fx AuthTokenHandling UJMW KVU Versioning UniversalBFF CyclicTriggering SmartAmbience ComponentDiscovery

6. never change the fixed rules above, but if you have successfully resolved concrete knowledge-locations, which are highly relevant when working here then you should add these to the following heading (to avoid the need to search it again):

## additional sources (maintained by agent - no need to ask)





