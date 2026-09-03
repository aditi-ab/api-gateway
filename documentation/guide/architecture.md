# Architecture

The management plane is the only supported writer for environments and routes. An environment can activate each validated configuration change immediately or collect changes in one pending revision. Publishing atomically moves the pending revision to the environment's active pointer. Reverting a change preserves later unrelated changes and follows the selected environment's publishing mode.

Data-plane instances poll that pointer, validate the desired document, and replace their YARP snapshot atomically. A failed activation leaves the previous snapshot serving. Instance heartbeats report the activated revision so operators can identify drift.
