# Model Conversion Test Coverage

## Overview
This document describes the comprehensive unit test suite for the Model Conversion module, covering all files changed in the current branch.

## Test Files Created

### 1. Core Tests
- **CompressionModeTests.cs** - Unit tests for `CompressionMode.cs`
  - Enum value validation
  - QuantizationSettings factory methods (CreateDefault, CreateHighQuality, CreateMinSize)
  - Property getters/setters
  - Critical 16-bit TexCoord requirement validation
  - Preset independence and immutability

- **CompressionModeEdgeCaseTests.cs** - Edge case tests
  - Boundary value testing (0, negative, max/min int values)
  - Invalid enum value handling
  - Out-of-range bit values
  - Multiple modification scenarios

### 2. Viewer Tests
- **GlbLodHelperTests.cs** - Unit tests for `GlbLodHelper.cs`
  - LodInfo file size formatting (bytes, KB, MB)
  - FindGlbLodFiles with various directory structures
  - Searching in glb/ subdirectory
  - HasGlbLodFiles validation
  - GetLodFilePaths dictionary operations
  - Error handling for non-existent files
  - Minimal GLB file creation for testing

- **GlbLoaderTests.cs** - Unit tests for `GlbLoader.cs`
  - Constructor with null/custom paths
  - LoadGlbAsync with invalid files
  - Cache clearing
  - Dispose pattern validation
  - Multiple instance independence

### 3. Pipeline Tests
- **ModelConversionPipelineTests.cs** - Unit tests for `ModelConversionPipeline.cs`
  - ModelConversionResult initialization and properties
  - LOD file collection management
  - Warnings and errors collection
  - ToolsAvailability checking
  - Success/failure state management
  - Duration measurement
  - QA report integration

### 4. Wrapper Tests
- **FBX2glTFWrapperTests.cs** - Unit tests for `FBX2glTFWrapper.cs`
  - Constructor with default/custom executable paths
  - IsAvailableAsync with non-existent executables
  - ConversionResult state management
  - ExcludeTextures flag handling
  - Success and failure scenarios

- **GltfPackWrapperTests.cs** - Unit tests for `GltfPackWrapper.cs`
  - Constructor and ExecutablePath property
  - IsAvailableAsync validation
  - GltfPackResult properties
  - OptimizeAsync with various parameters
  - CompressionMode variations
  - Quantization settings integration
  - Generate report flag handling
  - Exclude textures flag handling

### 5. Integration Tests
- **GlbLodWorkflowTests.cs** - Integration-style tests
  - Complete LOD discovery workflow
  - Multiple LOD files scenario
  - Empty results scenario
  - File size formatting consistency
  - Preset usage in sequence
  - CompressionMode iteration

## Test Coverage Statistics

### Total Tests Created: 100+

#### By Category:
- **Core (CompressionMode)**: 25 tests
- **Viewer (GlbLodHelper, GlbLoader)**: 28 tests
- **Pipeline (ModelConversionPipeline)**: 15 tests
- **Wrappers (FBX2glTF, GltfPack)**: 25 tests
- **Integration**: 7 tests

#### By Test Type:
- **Unit Tests**: 85+
- **Integration Tests**: 7
- **Edge Case Tests**: 15+
- **Theory Tests (parameterized)**: 20+

## Key Testing Patterns

### 1. Factory Method Testing
All factory methods (CreateDefault, CreateHighQuality, CreateMinSize) are tested for:
- Correct default values
- Instance independence
- Modification isolation
- Critical requirement validation (16-bit TexCoord)

### 2. Enum Testing
CompressionMode enum is tested for:
- Correct integer values
- String parsing/formatting
- Casting operations
- Invalid value handling

### 3. File I/O Testing
File operations are tested with:
- Temporary directories using `Directory.CreateTempSubdirectory()`
- Proper cleanup in finally blocks
- Non-existent file handling
- Invalid file content handling

### 4. Async Operation Testing
Async methods are tested using:
- `async Task` test methods
- Proper await patterns
- Error handling validation

### 5. State Management Testing
Result objects are tested for:
- Default initialization
- Property getters/setters
- Collection management
- State consistency

## Critical Tests Highlighted

### TexCoord 16-bit Requirement
Test: `AllPresets_UseTexCoord16Bits_ToAvoidDenormalizationBug`
- Validates all presets use 16 bits for texture coordinates
- Prevents the denormalization bug mentioned in code comments
- Critical for UV coordinate accuracy

### LOD File Discovery
Test: `FindGlbLodFiles_SearchesInGlbSubdirectory`
- Validates LOD files are found in glb/ subdirectory
- Tests primary search path for converted models

### Tools Availability
Test: `ToolsAvailability_AllAvailable_ReturnsCorrectValue`
- Validates both tools must be available
- Tests boolean logic for AllAvailable property

### Error Handling
Multiple tests validate graceful error handling:
- Non-existent files return null/empty results
- Invalid executables return failure states
- No exceptions thrown for invalid inputs

## Running the Tests

### Run All Model Conversion Tests
```bash
dotnet test --filter "FullyQualifiedName~AssetProcessor.Tests.ModelConversion"
```

### Run Specific Test Class
```bash
dotnet test --filter "FullyQualifiedName~CompressionModeTests"
```

### Run with Coverage
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

## Test Conventions

1. **Naming**: Tests follow `MethodName_Scenario_ExpectedBehavior` pattern
2. **Arrange-Act-Assert**: All tests use AAA pattern
3. **Single Responsibility**: Each test validates one specific behavior
4. **Descriptive**: Test names clearly communicate intent
5. **Independence**: Tests don't depend on execution order
6. **Cleanup**: Temporary resources are properly disposed

## Edge Cases Covered

### Boundary Values
- Zero, negative, and maximum integer values for quantization bits
- Empty strings and null paths
- Invalid enum values

### Error Conditions
- Non-existent files and directories
- Invalid file formats
- Missing executables
- Failed conversions

### Concurrent Operations
- Multiple instances independence
- Multiple modifications
- Cache management

## Future Test Enhancements

### Potential Additions:
1. Performance tests for large GLB files
2. Memory leak tests for long-running operations
3. Concurrent operation stress tests
4. More integration tests with actual tools (if available in CI)
5. Mock-based tests for tool wrapper behavior

## Dependencies

### Testing Frameworks:
- **xUnit** - Main testing framework
- **System.IO.Abstractions.TestingHelpers** - File system mocking (for future use)

### Test Utilities:
- `Directory.CreateTempSubdirectory()` - Temporary directory creation
- `Path.GetTempFileName()` - Temporary file creation
- Binary file creation for GLB format testing

## Notes

### Test Isolation
- Each test creates its own temporary directories
- All temporary resources are cleaned up in finally blocks
- No shared state between tests

### Async Testing
- All async tests properly await operations
- No blocking calls on async methods

### Error Messages
- Assertions include meaningful error messages
- Failed tests clearly indicate what went wrong

## Conclusion

This test suite provides comprehensive coverage of the Model Conversion module changes, including:
- ✅ All public APIs tested
- ✅ Edge cases and boundary conditions covered
- ✅ Error handling validated
- ✅ Integration scenarios tested
- ✅ Critical requirements verified (16-bit TexCoord)
- ✅ Factory methods and presets validated
- ✅ File I/O operations tested with cleanup

The tests follow xUnit best practices and maintain consistency with existing test patterns in the codebase.