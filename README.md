## Even Slow, Use Easier

Verity는 개발을 접해보지 않은 이들을 위한 쉽고 가벼운 2D게임엔진입니다.

Verity는 .NET 9.0 (C#) 기반으로, 통합 에디터를 포함합니다.
ECS기반의 구조와 C# 기반의 간편한 스크립팅을 지원합니다.

이 프로젝트는 현재 개발중입니다.

간단한 용어 정리

Project - 게임의 단위입니다. 여러 World와 Asset을 포함합니다.
World - 여러 Entity들이 존재하는 집합입니다. unity의 scene과 같습니다.
Asset - 게임 개발에 필요한 스크립트와 이미지 등을 말합니다. World 파일 또한 이에 포함됩니다.

Script - 실행 단위입니다. 가장 기본적인 클래스이며, LifeTime에 따라 실행되는 이벤트 함수를 포함합니다. unity의 MonoBehaviour와 같습니다.
